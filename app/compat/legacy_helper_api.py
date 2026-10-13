"""Finite, NI-only replacement for two legacy run_helper consumers.

Import performs no COM call. Only copied manifest-pinned helper VIs are Run.
Writes require a VI created or exclusively cloned through this module in this
Python process. Existing measurement VIs can only be used by metadata getters.
"""
from pathlib import Path
import hashlib
import json
import os
import shutil

HERE = Path(__file__).resolve().parent
APP = HERE.parent
ROOT = APP.parent.parent
STYLES_PATH = HERE / 'styles.json'
MANIFEST = json.loads((HERE / 'manifest.json').read_text(encoding='utf-8'))
NI_ROOT = Path(r'C:\Program Files (x86)\National Instruments\LabVIEW 2024\vi.lib')
WRITE_ROOTS = (HERE / 'tests', APP / 'projects', ROOT / 'Analysis/Validation_20260925/LabVIEW')
_sessions = {}
_owned = {}
TRACE_PATH = None

def _trace(name, stage):
    if TRACE_PATH is not None:
        with Path(TRACE_PATH).open('a', encoding='utf-8') as stream:
            stream.write(json.dumps({'helper': name, 'stage': stage}) + '\n')

# Values and output field names are the recorded original helper ABIs.
CONTRACTS = {
    'Native_Create': (('output_path',), (), False),
    'Native_GetRootDiagram': (('target_path',), ('diagram_id',), True),
    'Native_GetDiagramObjects': (('diagram_id',), ('object_ids',), True),
    'Native_GetObjectInfo': (('object_id',), ('class_name',), True),
    'Native_GetNodeTerminals': (('node_id',), ('terminal_ids',), True),
    'Native_GetTerminalName': (('terminal_id',), ('terminal_name',), True),
    'Native_GetWhileInfo': (('loop_id',), ('diagram_id', 'stop_id', 'counter_id'), True),
    'Native_GetForInfo': (('for_id',), ('diagram_id',), True),
    'Native_GetCaseInfo': (('case_id',), ('frame_ids', 'terminal_ids'), True),
    'Native_GetCaseFrameNames': (('case_id',), ('frame_names',), True),
    'Native_GetEventInfo_v2': (('event_id',), ('frame_ids',), True),
    'Native_AddNodeInDiagram': (('target_path', 'owner_diagram_id', 'style', 'x', 'y'), ('object_id', 'terminal_ids'), False),
    'Native_AddSubVIInDiagram': (('target_path', 'owner_diagram_id', 'subvi_path', 'x', 'y'), ('object_id', 'terminal_ids'), False),
    'Native_Wire': (('source_terminal', 'sink_terminal'), (), False),
    'Native_CreateControlFromTerminal_v2': (('terminal_id',), ('control_id', 'created_terminal_id'), False),
    'Native_CreateIndicatorFromTerminal': (('terminal_id',), ('control_id', 'created_terminal_id'), False),
    'Native_CreateConstantFromTerminal_v2': (('terminal_id',), ('constant_id', 'created_terminal_id'), False),
    'Native_SetControlLabel': (('control_id', 'label'), (), False),
    'Native_SetControlPosition': (('control_id', 'x', 'y'), (), False),
    'Native_MoveTerminal': (('terminal_id', 'owner_diagram_id', 'x', 'y'), (), False),
    'Native_SetDigitalNumericConstantValue': (('constant_id', 'value'), (), False),
    'Native_AssignConnectorTerminal': (('target_path', 'control_id', 'terminal_index'), (), False),
    'Own20261012_GetObjectIdentity': (('object_id',), ('uid', 'vi_path'), True),
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _path(value):
    raw = str(value)
    path = Path(raw)
    if not path.is_absolute() or ':' in raw[2:] or any(part in ('.', '..') or part.endswith((' ', '.')) for part in raw.replace('/', '\\').split('\\')[1:]):
        raise ValueError('Absolute unambiguous Windows file path required')
    for candidate in (path, *path.parents):
        if candidate.exists() and getattr(candidate.stat(follow_symlinks=False), 'st_file_attributes', 0) & 1024:
            raise ValueError('Reparse paths are forbidden')
    return path.resolve()


def _key(path):
    return str(path).casefold()


def _under(path, root):
    return _key(path).startswith(_key(root.resolve()) + os.sep)


def _new_target(value):
    path = _path(value)
    if path.suffix.casefold() != '.vi' or not any(_under(path, root) for root in WRITE_ROOTS):
        raise ValueError('New target must be an isolated .vi in a compatibility output root')
    if path.exists() or _key(path) in _owned:
        raise FileExistsError('Refusing to overwrite existing target: ' + str(path))
    if not path.parent.is_dir():
        raise FileNotFoundError('Target directory must already exist')
    return path


def copy_new_vi(source, target):
    """Exclusive clone of a blank created by this module, with tracked ownership."""
    source = _path(source)
    if _key(source) not in _owned or digest(source) != _owned[_key(source)]['initial_sha256']:
        raise ValueError('Only an unchanged VI created in this process can be cloned')
    target = _new_target(target)
    with source.open('rb') as reader, target.open('xb') as writer:
        shutil.copyfileobj(reader, writer)
    _owned[_key(target)] = {'path': str(target), 'initial_sha256': digest(target)}
    return target


def preflight():
    for name, entry in MANIFEST['helpers'].items():
        path = _path(HERE / entry['relative_path'])
        if not _under(path, HERE / 'helpers') or digest(path) != entry['sha256']:
            raise RuntimeError('Pinned compatibility helper changed: ' + name)
    if digest(STYLES_PATH) != MANIFEST['styles']['sha256']:
        raise RuntimeError('Compatibility styles changed')
    return {'helper_aliases': len(MANIFEST['helpers']), 'styles': MANIFEST['styles']['entries'], 'labview_called': False}


def _flag(vi, *names):
    for name in names:
        vi._FlagAsMethod(name)


def _open(app, path):
    app._FlagAsMethod('GetVIReference')
    vi = app.GetVIReference(str(path), '', False, 0)
    if _key(_path(vi.Path)) != _key(path):
        raise RuntimeError('NI resolved another module: ' + str(path))
    return vi


def _audit(app, initial):
    queue, seen, rows = list(initial), set(), []
    while queue:
        path = _path(queue.pop(0))
        if _key(path) in seen:
            continue
        seen.add(_key(path))
        if len(seen) > 512 or not _under(path, NI_ROOT) or any(token in _key(path) for token in ('virobotics', 'labviewbuilder', 'labviewbridge')):
            raise RuntimeError('Unreviewed or vendor helper dependency: ' + str(path))
        carrier = path
        if not carrier.is_file():
            carrier = next((p for p in path.parents if p.is_file() and p.suffix.casefold() in ('.vi', '.vim', '.llb')), None)
        if carrier is None or path.suffix.casefold() not in ('.vi', '.vim', '.ctl'):
            raise RuntimeError('Unsupported typed or missing NI dependency: ' + str(path))
        before = digest(carrier)
        vi = _open(app, path)
        callees = list(vi._CalleesPaths)
        queue.extend(callees)
        if digest(carrier) != before:
            raise RuntimeError('NI carrier changed during inspection')
        rows.append({'path': str(path), 'carrier': str(carrier), 'sha256': before, 'native_callees': callees})
    return rows


def _session(app):
    key = id(app)
    if key not in _sessions:
        _sessions[key] = {'app': app, 'refs': {}, 'helpers': {}, 'targets': {}, 'audits': {}}
    return _sessions[key]


def _helper(app, name):
    entry = MANIFEST['helpers'][name]
    path = _path(HERE / entry['relative_path'])
    if digest(path) != entry['sha256']:
        raise RuntimeError('Pinned helper bytes changed: ' + name)
    session = _session(app)
    vi = session['helpers'].get(name)
    if vi is None:
        vi = _open(app, path)
        session['helpers'][name] = vi
    if _key(_path(vi.Path)) != _key(path) or int(vi.ExecState) != 1:
        raise RuntimeError('Helper identity or compiled-idle state differs: ' + name)
    session['audits'][name] = _audit(app, vi._CalleesPaths)
    return vi


def _ref_input(key):
    return key.endswith('_id') or key in ('source_terminal', 'sink_terminal')


def run_helper(app, name, values, outputs=()):
    if name not in CONTRACTS:
        raise ValueError('Helper outside the two-consumer compatibility contract: ' + name)
    required, native_outputs, readonly = CONTRACTS[name]
    if set(values) != set(required) or not set(outputs).issubset(native_outputs):
        raise ValueError('Helper ABI field mismatch: ' + name)
    session = _session(app)
    origins = set()
    for field, value in values.items():
        if _ref_input(field):
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0 or value > 4294967295 or value not in session['refs']:
                raise ValueError('Unknown, null or stale compatibility reference: ' + field)
            origins.add(session['refs'][value])
        if field in ('x', 'y', 'terminal_index') and (isinstance(value, bool) or not isinstance(value, int) or not -32768 <= value <= 32767):
            raise ValueError('Int16 helper value required: ' + field)
        if field in ('style', 'value') and (isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 4294967295):
            raise ValueError('UInt32 helper value required: ' + field)
        if field == 'style' and value not in json.loads(STYLES_PATH.read_text(encoding='utf-8'))['ring']:
            raise ValueError('Style is outside the migrated NI style table')
    if 'target_path' in values:
        target = _path(values['target_path'])
        if not target.is_file() or target.suffix.casefold() != '.vi':
            raise ValueError('Existing target VI required')
        origins.add(_key(target))
    if len(origins) > 1:
        raise ValueError('References from different target VIs are forbidden')
    origin = next(iter(origins), None)
    created = _new_target(values['output_path']) if name == 'Native_Create' else None
    if not readonly and created is None and (origin is None or origin not in _owned):
        raise PermissionError('Writes require a target created by this compatibility process')
    if 'target_path' in values and _key(target) not in session['targets']:
        # Diagram/object handles require the owning VI reference to remain open.
        session['targets'][_key(target)] = _open(app, target)
    if 'subvi_path' in values:
        _audit(app, [values['subvi_path']])
    # Check the helper's own error before reading any resulting references.
    _trace(name, 'load_and_audit')
    vi = _helper(app, name)
    _flag(vi, 'SetControlValue', 'GetControlValue', 'Run')
    for field, value in values.items():
        if hasattr(app, '_oleobj_'):
            # Preserve the native ABI signedness instead of implicit VT_I4.
            import pythoncom
            import win32com.client
            if _ref_input(field) or field in ('style', 'value'):
                value = win32com.client.VARIANT(pythoncom.VT_UI4, int(value))
            elif field in ('x', 'y', 'terminal_index'):
                value = win32com.client.VARIANT(pythoncom.VT_I2, int(value))
        vi.SetControlValue(field, value)
    _trace(name, 'before_helper_run')
    vi.Run(False)
    _trace(name, 'helper_returned')
    error = vi.GetControlValue('error out')
    if error[0]:
        raise RuntimeError(name + ' original NI error: ' + repr(error))
    result = {field: vi.GetControlValue(field) for field in native_outputs}
    if created is not None:
        if not created.is_file():
            raise RuntimeError('Native_Create returned without its new target')
        _owned[_key(created)] = {'path': str(created), 'initial_sha256': digest(created)}
        session['targets'][_key(created)] = _open(app, created)
    for field, value in result.items():
        if field.endswith('_id') or field.endswith('_ids'):
            refs = value if field.endswith('_ids') else (value,)
            for ref in refs:
                ref = int(ref)
                if ref:
                    session['refs'][ref] = origin
    _trace(name, 'outputs_read')
    return {field: result[field] for field in outputs}


def audit_snapshot(app):
    session = _session(app)
    return {'helper_paths': {name: str(vi.Path) for name, vi in session['helpers'].items()},
            'helper_native_callees': dict(session['audits']),
            'owned_targets': list(_owned.values()), 'source_or_target_run_by_compat': False}


def close_session(app):
    session = _sessions.pop(id(app), None)
    if session:
        session.clear()
