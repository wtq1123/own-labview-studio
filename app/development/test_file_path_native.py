"""MCP18 selected-file preparation and owned root/For/Case placement.

Default --prepare-only is entirely offline. Execute only after an explicit NI
lease. All sources and generated callers remain unexecuted; fresh readers Run
only pinned own metadata helper VIs. Preserve failures; never retry a timeout.
"""
from pathlib import Path
import argparse
import hashlib
import json
import queue
import shutil
import subprocess
import sys
import threading
import uuid

APP = Path(__file__).resolve().parent.parent
NI = Path(r'C:\Program Files (x86)\National Instruments\LabVIEW 2024\vi.lib')


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    with Path(path).open('x', encoding='utf-8') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def hashed(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def processes():
    command = "@(Get-Process -Name LabVIEW -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{id=$_.Id;path=$_.Path;start_ticks=$_.StartTime.ToUniversalTime().Ticks} }) | ConvertTo-Json -Compress"
    result = subprocess.run(['powershell.exe', '-NoProfile', '-Command', command],
                            capture_output=True, text=True, encoding='utf-8', timeout=15)
    if result.returncode:
        raise RuntimeError('NI process identity unavailable: ' + result.stderr)
    data = json.loads(result.stdout) if result.stdout.strip() else []
    return data if isinstance(data, list) else [data]


def selection():
    manifest = read(APP / 'file-helpers/manifest.json')
    ctl = read(APP / 'tests/typefile-ctl-reopen-acceptance.json')['cases'][0]['source']
    global_vi = read(APP / 'tests/typefile-global-read-reopen-acceptance.json')['cases'][0]['source']
    selected = {
        'known_class': manifest['candidates']['class']['path'],
        'selected_class': str(NI / 'Comparison/Equal/Equal Comparable/Equal Comparable.lvclass'),
        'known_poly': manifest['candidates']['polymorphic']['path'],
        'selected_poly': str(NI / 'Bit Manipulation/Network-Order Bytes To Words Poly.vi'),
        'control': ctl,
        'global': global_vi,
    }
    for path in selected.values():
        if not Path(path).is_file():
            raise FileNotFoundError(path)
    return selected


class Mcp:
    def __init__(self, folder):
        self.folder = folder
        self.transcript = []
        self.counter = 0
        self.messages = queue.Queue()
        self.child = subprocess.Popen([str(APP / 'bin/OwnLabViewMcp.exe'), '--mcp'],
                                      cwd=str(APP), stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, text=True, encoding='utf-8',
                                      creationflags=subprocess.CREATE_NO_WINDOW)
        def reader():
            for line in self.child.stdout:
                self.messages.put(line)
            self.messages.put(None)
        threading.Thread(target=reader, daemon=True).start()

    def rpc(self, method, params):
        self.counter += 1
        request = {'jsonrpc': '2.0', 'id': self.counter, 'method': method, 'params': params}
        self.transcript.append({'request': request})
        self.child.stdin.write(json.dumps(request, ensure_ascii=False) + '\n')
        self.child.stdin.flush()
        try:
            line = self.messages.get(timeout=240)
        except queue.Empty:
            raise TimeoutError('MCP response timeout; NI helper state must be inspected before any retry.')
        if line is None:
            raise RuntimeError('MCP server ended before response')
        response = json.loads(line)
        self.transcript.append({'response': response})
        if 'error' in response:
            raise RuntimeError(repr(response['error']))
        return response['result']

    def call(self, name, arguments):
        result = self.rpc('tools/call', {'name': name, 'arguments': arguments})
        if result.get('isError'):
            raise RuntimeError(name + ': ' + repr(result))
        return json.loads(result['content'][0]['text'])

    def notify_ready(self):
        request = {'jsonrpc': '2.0', 'method': 'notifications/initialized'}
        self.transcript.append({'request': request})
        self.child.stdin.write(json.dumps(request) + '\n')
        self.child.stdin.flush()

    def close(self):
        self.child.stdin.close()
        try:
            self.child.wait(timeout=15)
        except subprocess.TimeoutExpired:
            # A worker may still own NI; do not kill/retry or hide that state.
            write(self.folder / 'client-shutdown-pending.json', {'pid': self.child.pid,
                  'ni_state_unknown': True, 'automatic_retry': False})
        write(self.folder / 'mcp-transcript.json', self.transcript)
        if self.child.poll() is not None:
            (self.folder / 'mcp.stderr.log').write_text(self.child.stderr.read(), encoding='utf-8')


def recipe(paths):
    steps = []
    def add(id, op, **args):
        steps.append({'id': id, 'op': op, 'args': args})
    add('loop', 'for', owner_diagram_id='$root.diagram_id', style='For Loop', x=200, y=140, bounds=[700, 350])
    add('loopInfo', 'for_info', for_id='$loop.for_id')
    add('loopTerms', 'for_terminals', for_id='$loop.for_id')
    add('count', 'constant', terminal_id='$loopTerms.count_id')
    add('countValue', 'set_number', constant_id='$count.constant_id', value=1)
    add('selector', 'control', style='Horizontal Toggle Switch', x=30, y=30, label='Selector')
    add('selectorTerm', 'control_terminal', control_id='$selector.object_id')
    add('branch', 'case', owner_diagram_id='$root.diagram_id', style='Case Structure', x=200, y=550, bounds=[700, 400])
    add('branchBefore', 'case_info', case_id='$branch.case_id')
    add('selectorWire', 'wire', source_terminal='$selectorTerm.terminal_id', sink_terminal='$branchBefore.terminal_ids.0')
    add('branchReady', 'case_selector_info', case_id='$branch.case_id')
    placements = [
        ('knownClassRoot', 'known_class', '$root.diagram_id', 60, 70),
        ('selectedClassFor', 'selected_class', '$loopInfo.diagram_id', 300, 230),
        ('knownClassCase', 'known_class', '$branchReady.frame_ids.0', 300, 640),
        ('knownPolyRoot', 'known_poly', '$root.diagram_id', 1050, 120),
        ('selectedPolyFor', 'selected_poly', '$loopInfo.diagram_id', 520, 300),
        ('controlCase', 'control', '$branchReady.frame_ids.1', 400, 720),
        ('globalRoot', 'global', '$root.diagram_id', 1050, 340),
        ('globalWriteCase', 'global', '$branchReady.frame_ids.0', 600, 780),
    ]
    for id, key, owner, x, y in placements:
        args = {'source_path': paths[key], 'owner_diagram_id': owner, 'x': x, 'y': y}
        if key == 'global':
            args.update(global_name='N', write=id == 'globalWriteCase')
        add(id, 'file_place', **args)
        if key in ('known_poly', 'selected_poly'):
            # Two independently selected Bytes-to-Words polys have input pin1.
            # Native terminal metadata is checked before/after save; no source Run.
            add(id + 'Input', 'constant', terminal_id='$' + id + '.terminal_ids.1')
        if id == 'globalWriteCase':
            add('globalWriteValue', 'constant', terminal_id='$globalWriteCase.terminal_ids.0')
            add('globalWriteNumber', 'set_double', constant_id='$globalWriteValue.constant_id', value=3.25)
        if id == 'globalRoot':
            add('globalReadSink', 'typed_indicator', terminal_id='$globalRoot.terminal_ids.0')
    return {'schema_version': 1, 'name': 'SelectedFilesAllDiagrams', 'steps': steps}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--ni-pid', type=int)
    parser.add_argument('--reader-exe', type=Path)
    args = parser.parse_args()
    selected = selection()
    if args.prepare_only:
        catalog = read(APP / 'recipes/operations.json')
        assert 'file_place' in catalog
        assert catalog['file_place']['inputs']['x'] == 'coordinate_i16'
        print(json.dumps({'offline_only': True, 'selected_sources': selected,
                         'sha256': {key: hashed(path) for key, path in selected.items()},
                         'planned_placements': 8, 'target_or_source_run': False}, ensure_ascii=False, indent=2))
        return 0
    if not args.ni_pid or not args.reader_exe or not args.reader_exe.is_file():
        parser.error('Execution requires --ni-pid and isolated --reader-exe')
    before = processes()
    if len(before) != 1 or before[0]['id'] != args.ni_pid:
        raise RuntimeError('Assigned NI process is unavailable: ' + repr(before))
    folder = APP / 'development/tests' / ('file_path_public_' + uuid.uuid4().hex)
    folder.mkdir()
    hashes = {path: hashed(path) for path in selected.values()}
    write(folder / 'original-source-snapshot.json', hashes)
    inputs = folder / 'independent-inputs'
    inputs.mkdir()
    paths = dict(selected)
    # Raw selected external file sources have no old generated/provenance report.
    for key, suffix in [('control', '.ctl'), ('global', '.vi')]:
        target = inputs / ('Independent' + key.capitalize() + '_' + uuid.uuid4().hex + suffix)
        shutil.copyfile(selected[key], target)
        assert hashed(target) == hashes[selected[key]]
        paths[key] = str(target)
        hashes[str(target)] = hashed(target)
    checks, prepared, results = [], {}, []
    def check(name, condition):
        checks.append({'name': name, 'passed': bool(condition)})
        if not condition:
            raise AssertionError(name)
    client = Mcp(folder)
    failure = None
    try:
        initialized = client.rpc('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {},
            'clientInfo': {'name': 'selected-file-path-acceptance', 'version': '1'}})
        check('MCP initialized', initialized['protocolVersion'] == '2025-06-18')
        client.notify_ready()
        tools = client.rpc('tools/list', {})['tools']
        check('18 public tools', len(tools) == 18 and sum(t['name'] == 'prepare_file' for t in tools) == 1)
        for key in ('selected_class', 'selected_poly', 'control', 'global'):
            row = client.call('prepare_file', {'source_path': paths[key], 'name': 'FilePath' + key.replace('_', '')})
            prepared[key] = row
            write(folder / (key + '-prepared.json'), row)
            check(key + ' actual kind', row['category'] == {'selected_class': 'class', 'selected_poly': 'polymorphic', 'control': 'control', 'global': 'global'}[key])
            check(key + ' source unchanged/no Run/Save', row['source_files_unchanged'] and not row['source_saved'] and not row['source_executed'] and not row['generated_vi_executed'])
            check(key + ' returned bytes match source', row['vi_sha256'] == hashed(row['vi_path']) and row['source_sha256'] == hashes[paths[key]])
            check(key + ' native identity and closure', row['native_identity_and_static_closure_checked'])
            paths[key] = row['vi_path']
        minimal = {'schema_version': 1, 'name': 'SelectedNiPolyRequiredInput', 'steps': [
            {'id': 'poly', 'op': 'file_place', 'args': {'source_path': paths['selected_poly'],
             'owner_diagram_id': '$root.diagram_id', 'x': 220, 'y': 120}},
            {'id': 'input', 'op': 'constant', 'args': {'terminal_id': '$poly.terminal_ids.1'}},
        ]}
        write(folder / 'minimal-selected-poly-recipe.json', minimal)
        check('Minimal selected NI poly preflight', client.call('validate_recipe', {'recipe': minimal})['valid'])
        minimal_result = client.call('build_recipe', {'recipe': minimal})
        results.append(minimal_result)
        write(folder / 'minimal-selected-poly-build.json', minimal_result)
        check('Actual NI poly original path plus typed required input compiles without Run', minimal_result['created'] and minimal_result['static_checks_passed'] and minimal_result['execution_state'] == 1 and not minimal_result['generated_vi_executed'])
        doc = recipe(paths)
        write(folder / 'recipe.json', doc)
        validated = client.call('validate_recipe', {'recipe': doc})
        check('Public recipe preflight', validated['valid'])
        result = client.call('build_recipe', {'recipe': doc})
        results.append(result)
        write(folder / 'build-result.json', result)
        check('Caller saved and compiled without Run', result['created'] and result['static_checks_passed'] and result['execution_state'] == 1 and not result['generated_vi_executed'])
        placed = [step['outputs'] for step in result['steps'] if step['operation'] == 'file_place']
        check('Eight real placements', len(placed) == 8)
        for number, row in enumerate(placed):
            check('Placement' + str(number) + ' source flags/hash', row['source_files_unchanged'] and not row['source_saved'] and not row['source_executed'] and hashed(row['source_path']) == row['source_sha256'])
            check('Placement' + str(number) + ' actual terminal owner and TD', bool(row['terminal_ids']) and len(row['terminal_ids']) == len(row['terminals']) and all(t['diagram_id'] == row['owner_diagram_id'] and len(t['type_descriptor']) >= 2 for t in row['terminals']))
        report_path = Path(result['vi_path']).parent / 'recipe-report.json'
    except Exception as ex:
        failure = repr(ex)
    finally:
        client.close()
    if failure is None:
        for phase in ('inspect', 'fresh'):
            output = folder / (phase + '-saved.json')
            try:
                command = [str(args.reader_exe), str(APP), str(report_path), str(output)]
                child = subprocess.run(command, cwd=str(APP), capture_output=True, text=True, encoding='utf-8',
                                       timeout=180, creationflags=subprocess.CREATE_NO_WINDOW)
                (folder / (phase + '.stdout.log')).write_text(child.stdout, encoding='utf-8')
                (folder / (phase + '.stderr.log')).write_text(child.stderr, encoding='utf-8')
                check(phase + ' saved metadata worker', child.returncode == 0)
                fresh = read(output)
                results.append(fresh)
                check(phase + ' saved caller hash and eight placements', fresh['passed'] and fresh['vi_sha256'] == result['vi_sha256'] and len(fresh['placements']) == 8)
                check(phase + ' root/For/Case actual membership', {'root', 'root/ForLoop', 'root/CaseStructure'} <= {r['scope'] for r in fresh['placements']})
                check(phase + ' no source/caller Run or Save', not any(fresh[k] for k in ('source_executed', 'source_saved', 'target_executed', 'target_saved')))
            except Exception as ex:
                failure = repr(ex)
                break
    after = processes()
    checks.append({'name': 'Same unique NI process retained', 'passed': before == after})
    checks.append({'name': 'All selected originals/raw inputs unchanged', 'passed': all(hashed(path) == digest for path, digest in hashes.items())})
    accepted = failure is None and all(row['passed'] for row in checks)
    report = {'passed': accepted, 'failure': failure, 'tool_count': 18, 'checks': checks,
              'prepared': prepared, 'results': results, 'ni_before': before, 'ni_after': after,
              'source_or_target_executed': False, 'folder': str(folder),
              'scope': 'Selected NI class/poly and independent control/global paths placed into actual owned root/For/Case diagrams through MCP18; saved static metadata and two fresh reader processes only. No hardware/dynamic-call/class-method behavior claimed.'}
    write(folder / 'acceptance.json', report)
    print(json.dumps({'passed': accepted, 'failure': failure, 'checks': len(checks), 'folder': str(folder)}, ensure_ascii=False, indent=2))
    return 0 if accepted else 1


if __name__ == '__main__':
    raise SystemExit(main())
