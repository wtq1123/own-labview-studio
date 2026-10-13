import json,uuid,os
from pathlib import Path
from test_file_path_native import APP,Mcp,write,read,hashed,processes
folder=APP/'tests'/('mixed-project_'+uuid.uuid4().hex);folder.mkdir();client=Mcp(folder);checks=[];failure=None;before=processes()
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'mixed-project','version':'1'}});client.notify_ready()
 method=Path(os.environ.get('OWN_METHOD_SUITE',str(APP/'tests/custom-methods_cde49b1dad4340faa48a0f361adb6362')))
 cls=read(method/'prepared.json')['vi_path'];body=read(method/'basebody.json')['vi_path'];typed=read(method/'typedclass.json')['vi_path']
 ctl=client.call('create_type_artifact',{'name':'ProjectParameters','kind':'control','controls':[{'kind':'numeric','label':'Gain','value':1.25}]});write(folder/'ctl.json',ctl)
 glob=client.call('create_type_artifact',{'name':'ProjectGlobal','kind':'global','controls':[{'kind':'boolean','label':'Enabled','value':True}]});write(folder/'global.json',glob)
 paths=[cls,body,ctl['vi_path'],glob['vi_path']];source={p:hashed(p) for p in paths+[typed]}
 project=client.call('create_project',{'name':'MixedClassProject','vi_paths':paths});write(folder/'project.json',project)
 copied=client.call('create_project',{'name':'MixedClassProjectEdited','source_project':project['project_path'],'vi_paths':[cls,body,ctl['vi_path'],typed]});write(folder/'copy.json',copied)
 checks=[{'name':'New project includes class VI CTL and global','passed':project['created'] and project['member_count']==4},{'name':'Copy edit replaces global by second class','passed':copied['created'] and copied['member_count']==4},{'name':'All member bytes and source project unchanged','passed':all(hashed(p)==h for p,h in source.items()) and hashed(project['project_path'])==project['project_sha256']}]
except Exception as e:failure=repr(e)
finally:client.close()
checks.append({'name':'NI session unchanged','passed':before==processes()});report={'passed':failure is None and all(c['passed'] for c in checks),'checks':checks,'failure':failure,'folder':str(folder),'window_operations_used':False};write(folder/'acceptance.json',report);print(json.dumps(report,ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
