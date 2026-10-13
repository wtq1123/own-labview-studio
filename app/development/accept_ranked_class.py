import json,uuid
from pathlib import Path
from test_file_path_native import APP,Mcp,write,processes
folder=APP/'tests'/('ranked-class_'+uuid.uuid4().hex);folder.mkdir()
before=processes(); client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append(dict(name=name,passed=bool(ok)))
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'ranked-class','version':'1'}});client.notify_ready()
 scalar=lambda kind:{'kind':kind,'label':'Element'}
 record={'kind':'cluster','label':'Record','children':[{'kind':'numeric','label':'Gain'},{'kind':'string','label':'Name'},{'kind':'array','label':'Flags','element':scalar('boolean')}]}
 fields=[{'kind':'array','label':'Matrix','dimensions':2,'element':scalar('numeric'),'value':[[1.25,2],[-3,4]]},
 {'kind':'array','label':'Cube','dimensions':3,'element':scalar('string'),'value':[[['甲','乙']],[['丙','丁']]]},
 {'kind':'array','label':'Records','element':record,'value':[[1.5,'一',[True,False]],[-2,'二',[]]]},
 {'kind':'array','label':'RecordGrid','dimensions':2,'element':record,'value':[[[3,'三',[False]]]]},
 {'kind':'array','label':'EmptyMatrix','dimensions':2,'element':scalar('boolean')}]
 request={'name':'RankedBase','kind':'class','controls':fields,'accessors':[{'field':f['label'],'dispatch':'dynamic','include_error_terminals':True} for f in fields]}
 write(folder/'base-request.json',request)
 base=client.call('create_type_artifact',request);write(folder/'base.json',base);check('Ranked and cluster-array fields compiled with native defaults',base['private_field_names_types_defaults_checked'])
 methods=[Path(base['accessor_members'][0][k]).name for k in ('read_path','write_path')]
 child=client.call('create_type_artifact',dict(name='RankedChild',kind='class',controls=[],parent_class=base['vi_path'],overrides=[{'method':m} for m in methods]));write(folder/'child.json',child)
 prepared=client.call('prepare_file',{'source_path':child['vi_path'],'name':'RankedCopy'});write(folder/'prepared.json',prepared);check('Ranked class copied with source bytes unchanged',prepared['source_files_unchanged'])
 changes={'Matrix':[[9.5],[-7],[0]],'Cube':[[['新']]],'Records':[[2.75,'改',[False,True]]],'RecordGrid':[[[0,'',[]],[9,'末',[True]]]],'EmptyMatrix':[[True,False]]}
 write(folder/'changed-values.json',changes)
 for label,field in [('ragged',dict(fields[0],value=[[1],[2,3]])),('rank',dict(fields[0],dimensions=0)),('bad cluster arity',dict(fields[2],value=[[1,'x']])),('partial empty shape',dict(fields[0],value=[[]]))]:
  prior=set((APP/'projects').iterdir());bad=client.rpc('tools/call',{'name':'create_type_artifact','arguments':dict(name='InvalidRanked',kind='class',controls=[field])});check(label+' rejected before authoring',bad.get('isError') is True and prior==set((APP/'projects').iterdir()))
except Exception as e:failure=repr(e)
finally:client.close()
checks.append(dict(name='NI session unchanged',passed=before==processes(),before=before,after=processes()))
report=dict(passed=failure is None and all(c['passed'] for c in checks),failure=failure,checks=checks,folder=str(folder));write(folder/'acceptance.json',report);print(json.dumps(report,ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
