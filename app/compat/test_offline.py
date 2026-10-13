"""Offline ABI, no-import-COM, ownership, provenance and consumer preservation tests."""
from pathlib import Path
import ast
import importlib.util
import json
import sys
import types
import uuid

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import legacy_helper_api as api

checks = []
def check(name, value):
    assert value, name
    checks.append({'name': name, 'passed': True})
def reject(name, fn, kind=Exception):
    try:
        fn()
    except kind:
        checks.append({'name': name, 'passed': True})
    else:
        raise AssertionError(name)

check('Import creates no COM module or session', 'win32com.client' not in sys.modules and not api._sessions)
check('23 helper aliases offline preflight', api.preflight()['helper_aliases'] == 23)
check('985 identical migrated styles', api.MANIFEST['styles']['legacy_entries_identical'] == 985)
for old, fixed in [('Native_CreateControlFromTerminal_v2','Own20261012_CreateControlFromTerminal'),
                   ('Native_CreateIndicatorFromTerminal','Own20261012_CreateIndicatorFromTerminal'),
                   ('Native_CreateConstantFromTerminal_v2','Own20261012_CreateConstantFromTerminal')]:
    check(old + ' repaired helper', api.MANIFEST['helpers'][old]['actual_helper'] == fixed)

folder = HERE / 'tests' / ('offline_' + uuid.uuid4().hex)
folder.mkdir(parents=True)
class FakeVI:
    def __init__(self, path):
        self.Path, self.ExecState, self._CalleesPaths = str(path), 1, []
        self.values, self.reads, self.run_count = {}, [], 0
        self.error = (False, 0, '')
    def _FlagAsMethod(self, name): pass
    def SetControlValue(self, name, value): self.values[name] = value
    def Run(self, wait):
        self.run_count += 1
        if self.Path.endswith('_Native_Create.vi'):
            Path(self.values['output_path']).write_bytes(b'fake isolated blank')
    def GetControlValue(self, name):
        self.reads.append(name)
        if name == 'error out': return self.error
        if name == 'diagram_id': return 11 if 'readonly' not in str(self.values.get('target_path','')) else 21
        return {'object_id': 12, 'terminal_ids': (13,14), 'control_id': 15,
                'created_terminal_id': 16, 'class_name': 'DigitalNumericConstant'}[name]
class FakeApp:
    def __init__(self): self.refs = {}
    def _FlagAsMethod(self, name): pass
    def GetVIReference(self, path, password, reserve, options):
        return self.refs.setdefault(path, FakeVI(path))
app = FakeApp()
target = folder / 'new.vi'
api.run_helper(app,'Native_Create',{'output_path':str(target)})
check('Only Native_Create registers a new target', api._key(target) in api._owned)
reject('Existing output rejected before helper execution', lambda: api.run_helper(app,'Native_Create',{'output_path':str(target)}), FileExistsError)
reject('Measurement output path rejected', lambda: api.run_helper(app,'Native_Create',{'output_path':str(api.ROOT/'original.vi')}), ValueError)
diagram = api.run_helper(app,'Native_GetRootDiagram',{'target_path':str(target)},('diagram_id',))['diagram_id']
result = api.run_helper(app,'Native_AddNodeInDiagram',{'target_path':str(target),'owner_diagram_id':diagram,'style':1050,'x':100,'y':100},('terminal_ids',))
check('Unrequested output refs are still tracked', 12 in api._session(app)['refs'])
control = api.run_helper(app,'Native_CreateControlFromTerminal_v2',{'terminal_id':int(result['terminal_ids'][0])},('control_id',))
check('Repaired control output remains old ABI', control['control_id'] == 15)
reject('Null reference rejected before helper', lambda: api.run_helper(app,'Native_GetObjectInfo',{'object_id':0},('class_name',)), ValueError)
reject('Caller arbitrary helper rejected', lambda: api.run_helper(app,'Native_RunMeasurement',{}), ValueError)
reject('Extra ABI field rejected', lambda: api.run_helper(app,'Native_Wire',{'source_terminal':13,'sink_terminal':14,'run':True}), ValueError)
reject('Style outside migrated table rejected', lambda: api.run_helper(app,'Native_AddNodeInDiagram',{'target_path':str(target),'owner_diagram_id':diagram,'style':4294967295,'x':0,'y':0}), ValueError)
reject('Int16 overflow rejected', lambda: api.run_helper(app,'Native_MoveTerminal',{'terminal_id':13,'owner_diagram_id':diagram,'x':32768,'y':0}), ValueError)
readonly = folder / 'readonly.vi'
readonly.write_bytes(b'existing original')
before = api.digest(readonly)
ro = api.run_helper(app,'Native_GetRootDiagram',{'target_path':str(readonly)},('diagram_id',))['diagram_id']
reject('Existing readonly target cannot be edited', lambda: api.run_helper(app,'Native_AddNodeInDiagram',{'target_path':str(readonly),'owner_diagram_id':ro,'style':1050,'x':0,'y':0}), PermissionError)
reject('Cross-module references rejected', lambda: api.run_helper(app,'Native_MoveTerminal',{'terminal_id':13,'owner_diagram_id':ro,'x':0,'y':0}), ValueError)
check('Existing readonly file unchanged', api.digest(readonly) == before)
clone = folder / 'clone.vi'
api.copy_new_vi(target, clone)
check('Exclusive copy registered with same source bytes', api.digest(target) == api.digest(clone) and api._key(clone) in api._owned)
reject('Copy refuses existing output', lambda: api.copy_new_vi(target,clone), FileExistsError)
reject('Copy rejects preexisting source', lambda: api.copy_new_vi(readonly,folder/'badcopy.vi'), ValueError)
reject('Vendor recursive dependency refused', lambda: api._audit(app,[api.NI_ROOT/'VIRobotics/Injected.vi']), RuntimeError)
helper = api._helper(app,'Native_CreateControlFromTerminal_v2')
helper.error = (True,1074,'original create failure')
helper.reads.clear()
reject('Original NI creation error preserved', lambda: api.run_helper(app,'Native_CreateControlFromTerminal_v2',{'terminal_id':13},('control_id',)), RuntimeError)
check('No reference output read after NI error', helper.reads == ['error out'])
helper.error = (False,0,'')
helper.ExecState = 2
reject('Broken helper rejected before Run', lambda: api._helper(app,'Native_CreateControlFromTerminal_v2'), RuntimeError)
helper.ExecState = 1
helper.Path = str(target)
reject('Loaded helper path collision rejected', lambda: api._helper(app,'Native_CreateControlFromTerminal_v2'), RuntimeError)

migration = json.loads(Path(json.loads((HERE/'migration-latest.json').read_text())['report']).read_text())
for row in migration['scripts']:
    source, backup = Path(row['source']), Path(row['backup'])
    check(source.name+' exact backup', api.digest(backup) == row['sha256_before'])
    compile(source.read_text(encoding='utf-8'),str(source),'exec')
    check(source.name+' offline compile', True)
    check(source.name+' migrated import', 'from legacy_helper_api import' in source.read_text(encoding='utf-8'))
# Inspection import with a stub connector; main() is deliberately not called.
stub = types.ModuleType('win32com')
stub.client = types.ModuleType('win32com.client')
stub.client.Dispatch = lambda *args: (_ for _ in ()).throw(AssertionError('Unexpected import COM'))
sys.modules['win32com'], sys.modules['win32com.client'] = stub, stub.client
source = Path(migration['scripts'][0]['source'])
spec = importlib.util.spec_from_file_location('migrated_inspection_offline',source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
check('Inspection consumer import does not attach NI', True)
report = {'passed':True,'check_count':len(checks),'checks':checks,'labview_called':False,
          'actual_consumers_executed':False,'signal_output_validated':False,'native_acceptance_pending':api.MANIFEST.get('native_acceptance_pending', True)}
(HERE/'offline-acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'passed':True,'check_count':len(checks),'labview_called':False}))
