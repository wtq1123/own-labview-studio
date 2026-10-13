"""NI authoring slot only: repair an isolated copy's NewVIObject error gate.

Never Run the source helper or its clone. Only pinned metadata/wire helpers Run.
"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
import sys
import uuid
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
import legacy_helper_api as api

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--ni-pid',required=True,type=int)
    a=p.parse_args()
    import test_native
    before=test_native.processes()
    assert len(before)==1 and before[0]['id']==a.ni_pid
    api.preflight()
    entry=api.MANIFEST['helpers']['Native_AddNodeInDiagram']
    source=(HERE/entry['relative_path']).resolve()
    assert api.digest(source)==entry['sha256']
    folder=HERE/'tests'/('guarded_node_'+uuid.uuid4().hex)
    folder.mkdir(parents=True)
    clone=folder/'OwnCompatGuard20261012_Native_AddNodeInDiagram.vi'
    shutil.copyfile(source,clone)
    # This separately authorized authoring copy is created here, never an old VI.
    api._owned[api._key(clone)]={'path':str(clone),'initial_sha256':api.digest(clone)}
    api.TRACE_PATH=folder/'authoring-trace.jsonl'
    import win32com.client
    app=win32com.client.Dispatch('LabVIEW.Application')
    assert test_native.processes()==before
    try:
        vi=api._open(app,clone)
        d=int(api.run_helper(app,'Native_GetRootDiagram',{'target_path':str(clone)},('diagram_id',))['diagram_id'])
        ids=api.run_helper(app,'Native_GetDiagramObjects',{'diagram_id':d},('object_ids',))['object_ids']
        nodes=[]
        for oid in ids:
            oid=int(oid)
            kind=str(api.run_helper(app,'Native_GetObjectInfo',{'object_id':oid},('class_name',))['class_name'])
            if kind in ('Wire','Label','Term') or 'Constant' in kind:
                continue
            try:
                terms=api.run_helper(app,'Native_GetNodeTerminals',{'node_id':oid},('terminal_ids',))['terminal_ids']
                pins={str(api.run_helper(app,'Native_GetTerminalName',{'terminal_id':int(t)},('terminal_name',))['terminal_name']):int(t) for t in terms}
                nodes.append({'id':oid,'kind':kind,'pins':pins})
            except Exception:
                continue
        creation=[n for n in nodes if {'vi object class','object refnum','error out'}<=set(n['pins'])]
        readers=[n for n in nodes if n['kind']=='Property' and {'reference','error in (no error)','error out'}<=set(n['pins']) and any(key in n['pins'] for key in ('Terms[]','Terminal','接线端[]'))]
        assert len(creation)==len(readers)==1,json.dumps(nodes,ensure_ascii=False)
        api.run_helper(app,'Native_Wire',{'source_terminal':creation[0]['pins']['error out'],'sink_terminal':readers[0]['pins']['error in (no error)']})
        api._flag(vi,'SaveInstrument')
        vi.SaveInstrument(str(clone),False,False)
        assert int(vi.ExecState)==1
        callees=api._audit(app,vi._CalleesPaths)
        newpath=HERE/'helpers'/clone.name
        assert not newpath.exists()
        shutil.copyfile(clone,newpath)
        new=dict(entry,relative_path='helpers/'+newpath.name,sha256=api.digest(newpath),
                 source_path=str(source),source_sha256=entry['sha256'],
                 isolated_repair='NewVIObject.error out -> Terms[].error in; creation error retains original final output',
                 native_creation_node=creation[0],native_guarded_reader=readers[0],native_callees=callees)
        manifest_path=HERE/'manifest.json'
        backup=folder/'manifest.before-node-guard.json'
        shutil.copyfile(manifest_path,backup)
        manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
        manifest['helpers']['Native_AddNodeInDiagram']=new
        manifest['node_guard_authoring']={'folder':str(folder),'source_sha256':entry['sha256'],'guarded_sha256':new['sha256'],'source_or_guarded_helper_executed':False}
        manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
        report={'passed':True,'source_sha256':api.digest(source),'created_sha256':api.digest(newpath),'source_preserved':api.digest(source)==entry['sha256'],
                'execution_state':1,'creation':creation[0],'reader':readers[0],'native_callees':callees,
                'source_or_guarded_helper_executed':False,'ni_process_before':before,'ni_process_after':test_native.processes()}
        assert report['ni_process_before']==report['ni_process_after']
        (folder/'authoring.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({'passed':True,'folder':str(folder),'new_helper':str(newpath)},ensure_ascii=False))
        return 0
    finally:
        api.close_session(app)
        app=None
if __name__=='__main__':
    raise SystemExit(main())
