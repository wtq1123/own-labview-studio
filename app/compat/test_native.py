"""Explicit NI lease only: new finite Add VI=5 and fresh read-only metadata worker."""
from pathlib import Path
import argparse
import json
import subprocess
import sys
import uuid

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import legacy_helper_api as api

def processes():
    command = "@(Get-Process -Name LabVIEW -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{id=$_.Id;path=$_.Path;start_ticks=$_.StartTime.ToUniversalTime().Ticks} }) | ConvertTo-Json -Compress"
    result = subprocess.run(['powershell.exe','-NoProfile','-Command',command],capture_output=True,text=True,encoding='utf-8',timeout=15)
    data = json.loads(result.stdout) if result.stdout.strip() else []
    return data if isinstance(data,list) else [data]

def metadata(app,target):
    vi = api._open(app,target)
    d = api.run_helper(app,'Native_GetRootDiagram',{'target_path':str(target)},('diagram_id',))['diagram_id']
    ids = api.run_helper(app,'Native_GetDiagramObjects',{'diagram_id':int(d)},('object_ids',))['object_ids']
    objects = []
    for ref in ids:
        ref = int(ref)
        kind = api.run_helper(app,'Native_GetObjectInfo',{'object_id':ref},('class_name',))['class_name']
        identity = api.run_helper(app,'Own20261012_GetObjectIdentity',{'object_id':ref},('uid','vi_path'))
        assert Path(identity['vi_path']).resolve() == target.resolve()
        objects.append({'class':str(kind),'uid':int(identity['uid']),'module':str(identity['vi_path'])})
    return {'path':str(target),'sha256':api.digest(target),'execution_state':int(vi.ExecState),
            'native_callees':list(vi._CalleesPaths),'objects':objects,'helpers':api.audit_snapshot(app)}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ni-pid',required=True,type=int)
    parser.add_argument('--phase',choices=('build','reopen'))
    parser.add_argument('--folder',type=Path)
    args = parser.parse_args()
    if not args.phase:
        folder = HERE/'tests'/('native_'+uuid.uuid4().hex)
        folder.mkdir(parents=True)
        for phase in ('build','reopen'):
            child = subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).resolve()),'--ni-pid',str(args.ni_pid),'--phase',phase,'--folder',str(folder)],capture_output=True,text=True,encoding='utf-8',timeout=180,creationflags=subprocess.CREATE_NO_WINDOW)
            (folder/(phase+'.stdout.txt')).write_text(child.stdout,encoding='utf-8')
            (folder/(phase+'.stderr.txt')).write_text(child.stderr,encoding='utf-8')
            if child.returncode:
                print(json.dumps({'passed':False,'phase':phase,'folder':str(folder),'error':child.stderr},ensure_ascii=False))
                return 1
        first = json.loads((folder/'build.json').read_text(encoding='utf-8'))
        second = json.loads((folder/'reopen.json').read_text(encoding='utf-8'))
        assert first['snapshot']['sha256'] == second['snapshot']['sha256']
        assert sorted((o['class'],o['uid'],o['module']) for o in first['snapshot']['objects']) == sorted((o['class'],o['uid'],o['module']) for o in second['snapshot']['objects'])
        report = {'passed':True,'finite_arithmetic_output':5,'build':first,'fresh_worker':second,
                  'original_consumers_executed':False,'measurement_main_loaded_or_executed':False,
                  'complete_historical_fft_or_inspection_validated':False,
                  'scope':'The migrated Python compatibility helper surface on a new finite Add VI and fresh metadata reader only.'}
        (folder/'acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        (HERE/'native-latest.json').write_text(json.dumps({'report':str(folder/'acceptance.json'),'passed':True},ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({'passed':True,'folder':str(folder),'finite_arithmetic_output':5,'fresh_worker':True},ensure_ascii=False))
        return 0
    before = processes()
    assert len(before)==1 and before[0]['id']==args.ni_pid
    api.preflight()
    import win32com.client
    app = None
    folder = args.folder.resolve()
    api.TRACE_PATH = folder / (args.phase + '-helper-trace.jsonl')
    target = folder/'FiniteAdd.vi'
    try:
        app = win32com.client.Dispatch('LabVIEW.Application')
        assert processes()==before, 'NI Dispatch started another process'
        if args.phase=='build':
            api.run_helper(app,'Native_Create',{'output_path':str(target)})
            clone = folder/'ExclusiveBlankCopy.vi'
            api.copy_new_vi(target,clone)
            d = int(api.run_helper(app,'Native_GetRootDiagram',{'target_path':str(target)},('diagram_id',))['diagram_id'])
            node = api.run_helper(app,'Native_AddNodeInDiagram',{'target_path':str(target),'owner_diagram_id':d,'style':1050,'x':220,'y':100},('object_id','terminal_ids'))
            pins = {str(api.run_helper(app,'Native_GetTerminalName',{'terminal_id':int(ref)},('terminal_name',))['terminal_name']):int(ref) for ref in node['terminal_ids']}
            for pin,label,indicator in [('x','A',False),('y','B',False),('x+y','Sum',True)]:
                helper = 'Native_CreateIndicatorFromTerminal' if indicator else 'Native_CreateControlFromTerminal_v2'
                control = api.run_helper(app,helper,{'terminal_id':pins[pin]},('control_id','created_terminal_id'))
                api.run_helper(app,'Native_SetControlLabel',{'control_id':int(control['control_id']),'label':label})
                api.run_helper(app,'Native_SetControlPosition',{'control_id':int(control['control_id']),'x':40 if not indicator else 300,'y':40 if label=='A' else 120})
                api.run_helper(app,'Native_MoveTerminal',{'terminal_id':int(control['created_terminal_id']),'owner_diagram_id':d,'x':100 if not indicator else 400,'y':80 if label=='A' else 160})
            second = api.run_helper(app,'Native_AddNodeInDiagram',{'target_path':str(target),'owner_diagram_id':d,'style':1050,'x':220,'y':300},('terminal_ids',))
            second_pins = {str(api.run_helper(app,'Native_GetTerminalName',{'terminal_id':int(ref)},('terminal_name',))['terminal_name']):int(ref) for ref in second['terminal_ids']}
            for pin,value in [('x',2),('y',3)]:
                constant = api.run_helper(app,'Native_CreateConstantFromTerminal_v2',{'terminal_id':second_pins[pin]},('constant_id',))
                api.run_helper(app,'Native_SetDigitalNumericConstantValue',{'constant_id':int(constant['constant_id']),'value':value})
            vi = api._open(app,target)
            api._flag(vi,'SetControlValue','GetControlValue','MakeCurValueDefault','SaveInstrument','Run')
            vi.SetControlValue('A',2.0)
            vi.SetControlValue('B',3.0)
            vi.MakeCurValueDefault()
            vi.SaveInstrument(str(target),False,False)
            frozen = api.digest(target)
            assert int(vi.ExecState)==1
            vi.Run(False)  # Explicitly authorized new finite arithmetic target only.
            assert float(vi.GetControlValue('Sum'))==5.0
            assert api.digest(target)==frozen
            snap = metadata(app,target)
            report = {'passed':True,'phase':'build','snapshot':snap,'finite_result':5,'only_new_finite_arithmetic_executed':True,'original_consumers_executed':False,'source_measurement_run':False}
        else:
            first = json.loads((folder/'build.json').read_text(encoding='utf-8'))
            before_hash = api.digest(target)
            snap = metadata(app,target)
            assert before_hash==first['snapshot']['sha256']==snap['sha256'] and snap['execution_state']==1
            report = {'passed':True,'phase':'reopen','snapshot':snap,'source_measurement_run':False,'target_run':False,'target_saved':False,'fresh_worker':True}
        report['ni_process_before'], report['ni_process_after'] = before,processes()
        assert report['ni_process_after']==before
        (folder/(args.phase+'.json')).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        return 0
    finally:
        if app is not None:
            api.close_session(app)
        app=None

if __name__=='__main__':
    raise SystemExit(main())
