import uuid,json
from pathlib import Path
from test_file_path_native import APP,Mcp,read,write,hashed,processes
folder=APP/'tests'/('libraries_'+uuid.uuid4().hex);folder.mkdir();before=processes();client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append(dict(name=name,passed=bool(ok)))
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'library-acceptance','version':'1'}});client.notify_ready()
 body=client.call('build_recipe',{'recipe':read(APP/'recipes/connector-auto-sum.json')});write(folder/'body.json',body)
 ctl=client.call('create_type_artifact',dict(name='LibraryRecord',kind='control',controls=[dict(kind='numeric',label='Value',value=4)]));write(folder/'ctl.json',ctl)
 glob=client.call('create_type_artifact',dict(name='LibraryGlobal',kind='global',controls=[dict(kind='string',label='Text',value='Hello')]));write(folder/'global.json',glob)
 cls=client.call('create_type_artifact',dict(name='LibraryClass',kind='class',controls=[dict(kind='numeric',label='Value',value=3)],accessors=[dict(field='Value',dispatch='dynamic')]));write(folder/'class.json',cls)
 lib=client.call('create_library',dict(name='TestLibrary',member_paths=[r['vi_path'] for r in [body,ctl,glob,cls]]));write(folder/'library.json',lib)
 check('Native mixed library has compiled qualified members',len(lib['added_members'])==4 and all(r['execution_state']==1 for r in lib['native_members'] if r.get('vi_type')==1))
 nested=client.call('create_library',dict(name='OuterLibrary',member_paths=[lib['library_path']]));write(folder/'nested.json',nested)
 check('Nested library copied and source retained',nested['source_files_unchanged'])
 remove=lib['added_members'][1]['name'];copied=client.call('create_library',dict(name='EditedLibrary',source_library=lib['library_path'],remove_members=[remove],member_paths=[body['vi_path']]));write(folder/'copied.json',copied)
 check('Copy edit removes selected member and adds new copy',remove not in [r['name'] for r in copied['native_members']] and len(copied['added_members'])==1)
 project=client.call('create_project',dict(name='LibraryProject',vi_paths=[nested['library_path'],copied['library_path']],member_folders={nested['library_path']:'Libraries/Nested'}));write(folder/'project.json',project)
 edited=client.call('create_project',dict(name='LibraryProjectCopy',source_project=project['project_path'],vi_paths=[nested['library_path'],copied['library_path']]));write(folder/'project-copy.json',edited)
 check('Library projects create and copy preserving layout',project['layout']==edited['layout'] and edited['source_vis_unchanged'])
except Exception as e:failure=repr(e)
finally:client.close()
checks.append(dict(name='NI session unchanged',passed=before==processes()))
report=dict(passed=failure is None and all(r['passed'] for r in checks),failure=failure,checks=checks,folder=str(folder));write(folder/'acceptance.json',report);print(json.dumps(report));raise SystemExit(0 if report['passed'] else 1)
