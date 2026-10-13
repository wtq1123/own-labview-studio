import uuid,json,copy
from pathlib import Path
from test_file_path_native import APP,Mcp,read,write,hashed,processes
folder=APP/'tests'/('build-specs_'+uuid.uuid4().hex);folder.mkdir();before=processes();client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append(dict(name=name,passed=bool(ok)))
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'build-specs','version':'1'}});client.notify_ready()
 body=client.call('build_recipe',{'recipe':read(APP/'recipes/connector-auto-sum.json')});write(folder/'body.json',body);source=body['vi_path'];original=hashed(source)
 specs=[dict(name='Finite'+k,kind=k,source_path=source) for k in ['EXE','DLL','SDIST']]
 first=client.call('create_project',dict(name='BuildSpecs',vi_paths=[source],folders=['Empty/Nested'],member_folders={source:'Logic/Arithmetic'},build_specs=specs,build=True));write(folder/'first.json',first)
 check('EXE DLL and source distribution actually built',len(first['build_results'])==3 and all(r['built'] and r['files'] for r in first['build_results']))
 old_hash=hashed(first['project_path']);old_files={r['path']:hashed(r['path']) for b in first['build_results'] for r in b['files']}
 copied=client.call('create_project',dict(name='CopiedBuildSpecs',vi_paths=[source],source_project=first['project_path'],build=True));write(folder/'copied.json',copied)
 check('Virtual folders preserved in copy',copied['layout']==first['layout'])
 check('Copied specifications rebuild to new destinations',all(r['built'] for r in copied['build_results']) and {r['destination'] for r in copied['build_results']}.isdisjoint(r['destination'] for r in first['build_results']))
 modified=copy.deepcopy(specs);modified[0]['output_name']='Changed.exe';modified[1]['output_name']='Changed.dll'
 changed=client.call('create_project',dict(name='ModifiedBuildSpecs',vi_paths=[source],source_project=first['project_path'],build_specs=modified,build=True));write(folder/'changed.json',changed)
 check('Modified EXE and DLL names built',all(Path(r['destination'],out).is_file() for r,out in zip(changed['build_results'][:2],['Changed.exe','Changed.dll'])))
 check('Original project and outputs unchanged',old_hash==hashed(first['project_path']) and all(hashed(p)==h for p,h in old_files.items()))
 check('Source VI unchanged',original==hashed(source))
except Exception as e:failure=repr(e)
finally:client.close()
checks.append(dict(name='NI session unchanged',passed=before==processes()))
report=dict(passed=failure is None and all(r['passed'] for r in checks),failure=failure,checks=checks,folder=str(folder));write(folder/'acceptance.json',report);print(json.dumps(report));raise SystemExit(0 if report['passed'] else 1)
