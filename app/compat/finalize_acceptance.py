"""Offline acceptance promotion; never imports or calls COM."""
from pathlib import Path
import hashlib
import json
import shutil
HERE=Path(__file__).resolve().parent
latest=json.loads((HERE/'native-latest.json').read_text(encoding='utf-8'))
path=Path(latest['report'])
report=json.loads(path.read_text(encoding='utf-8'))
assert report['passed'] and report['finite_arithmetic_output']==5
assert report['fresh_worker']['fresh_worker'] and not report['fresh_worker']['target_run'] and not report['fresh_worker']['target_saved']
assert not report['measurement_main_loaded_or_executed'] and not report['original_consumers_executed']
first,second=report['build']['snapshot'],report['fresh_worker']['snapshot']
assert first['sha256']==second['sha256']==hashlib.sha256(Path(first['path']).read_bytes()).hexdigest()
assert first['execution_state']==second['execution_state']==1
for phase in (report['build'],report['fresh_worker']):
    assert phase['ni_process_before']==phase['ni_process_after']
manifest_path=HERE/'manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
backup=HERE/'manifest.before-native-acceptance.json'
if not backup.exists():
    shutil.copyfile(manifest_path,backup)
manifest['native_acceptance_pending']=False
manifest['native_acceptance']={'report':str(path),'report_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                               'finite_arithmetic_result':5,'fresh_worker_readonly':True,
                               'original_consumers_executed':False,'measurement_main_loaded_or_executed':False,
                               'helpers_exercised':list(first['helpers']['helper_paths']),
                               'full_historical_inspection_or_fft_outputs_validated':False}
manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
failure=HERE/'tests/native_1269f3bd5195461f9f41f1c67431c0ba/failure-summary.json'
failure.write_text(json.dumps({'status':'first_attempt_timeout_preserved','timeout_seconds':180,
    'ni_pid':44972,'all_python_clients_exited':True,'helper_remained_running':'OwnCompat20261012_Native_AddNodeInDiagram.vi',
    'helper_observed_execution_state':2,'readonly_state_evidence':str(HERE/'native-idle-inspection.stdout.txt'),
    'measurement_main_loaded_or_run':False,'generated_target_run':False,
    'observed_code_issues':['Initial runner did not retain an owning target VI reference unlike the historical builder.',
                            'Original node creation helper did not gate Terms[] with NewVIObject error output.',
                            'Implicit Python integer marshaling was replaced by explicit UI4/I2 ABI values.'],
    'causal_limit':'The first synchronous helper call never returned its original NI error; the three corrections were accepted together and were not causally isolated.',
    'recovery':'Parent performed the authorized hidden NI restart; subsequent accepted fixture used fresh PID36800 and fresh references.'},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(manifest['native_acceptance'],ensure_ascii=False,indent=2))
