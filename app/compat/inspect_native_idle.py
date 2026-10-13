"""Read-only bounded recovery inspection; this script never calls Run or Save."""
from pathlib import Path
import json
import subprocess
import sys
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
if '--worker' not in sys.argv:
    try:
        result=subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).resolve()),'--worker'],capture_output=True,text=True,encoding='utf-8',timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
        (HERE/'native-idle-inspection.stdout.txt').write_text(result.stdout,encoding='utf-8')
        (HERE/'native-idle-inspection.stderr.txt').write_text(result.stderr,encoding='utf-8')
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(result.returncode)
    except subprocess.TimeoutExpired as exc:
        output=exc.stdout or b''
        (HERE/'native-idle-inspection.stdout.txt').write_bytes(output if isinstance(output,bytes) else output.encode('utf-8'))
        (HERE/'native-idle-inspection-timeout.json').write_text(json.dumps({'timeout_seconds':30,'source_or_target_run':False}),encoding='utf-8')
        raise
else:
    import legacy_helper_api as api
    import win32com.client
    app=win32com.client.Dispatch('LabVIEW.Application')
    print(json.dumps({'attached':True}),flush=True)
    app._FlagAsMethod('GetVIReference')
    for name in ('Native_Create','Native_GetRootDiagram','Native_AddNodeInDiagram','Native_GetTerminalName','Native_CreateControlFromTerminal_v2','Native_CreateIndicatorFromTerminal','Native_CreateConstantFromTerminal_v2','Native_SetControlPosition','Native_MoveTerminal'):
        path=(HERE/api.MANIFEST['helpers'][name]['relative_path']).resolve()
        print(json.dumps({'about_to_read':name}),flush=True)
        vi=app.GetVIReference(str(path),'',False,0)
        print(json.dumps({'helper':name,'actual_path':str(vi.Path),'execution_state':int(vi.ExecState)}),flush=True)
        vi=None
    app=None
