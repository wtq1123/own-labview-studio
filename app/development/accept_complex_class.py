import copy,json,uuid
from pathlib import Path
from test_file_path_native import APP,Mcp,write,hashed,processes
folder=APP/'tests'/('complex-class_'+uuid.uuid4().hex);folder.mkdir()
before=processes(); client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append({'name':name,'passed':bool(ok)})
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'complex-class','version':'1'}});client.notify_ready()
 field={'kind':'cluster','label':'Settings','children':[{'kind':'numeric','label':'Gain'},{'kind':'cluster','label':'Options','children':[{'kind':'boolean','label':'Enabled'},{'kind':'string','label':'Title'},{'kind':'array','label':'Weights','element':{'kind':'numeric','label':'Element'}}]}],'value':[2.5,[True,'复杂类默认值',[0.25,0.75]]]}
 arrays=[{'kind':'array','label':label,'element':{'kind':kind,'label':'Element'},'value':value} for label,kind,value in [('Samples','numeric',[1.25,-2,0]),('Flags','boolean',[True,False]),('Names','string',['甲','乙']),('Empty','numeric',[])]]
 request={'name':'ComplexBase','kind':'class','controls':[field]+arrays, 'accessors':[{'field':f['label'],'dispatch':'dynamic','include_error_terminals':True} for f in [field]+arrays]}
 write(folder/'base-request.json',request)
 base=client.call('create_type_artifact',request);write(folder/'base.json',base)
 check('Nested cluster default persists in native private data',base['native_private_fields'][0]['value']==field['value'])
 check('Complex dynamic read/write accessors compile',len(base['accessor_members'])==5)
 check('Typed and empty array defaults persist',all(next(f['value'] for f in base['native_private_fields'] if f['label']==a['label'])==a['value'] for a in arrays))
 originals={p:hashed(p) for p in [base['vi_path']]+[base['accessor_members'][0][k] for k in ('read_path','write_path')]}
 methods=[Path(base['accessor_members'][0][k]).name for k in ('read_path','write_path')]
 child_req={'name':'ComplexChild','kind':'class','controls':[],'parent_class':base['vi_path'],'overrides':[{'method':m} for m in methods]}
 child=client.call('create_type_artifact',child_req);write(folder/'child.json',child);write(folder/'child-request.json',child_req)
 check('Child complex accessor overrides compile',len(child['override_members'])==2)
 prepared=client.call('prepare_file',{'source_path':child['vi_path'],'name':'ComplexMigrated'});write(folder/'prepared.json',prepared)
 check('Complex hierarchy copied with native parent checked',prepared['inheritance_closure_certified'] and prepared['source_files_unchanged'])
 for name,bad in [('Cluster arity rejected',dict(request,controls=[dict(field,value=[2.5])])),('Nested scalar type mismatch rejected',dict(request,controls=[dict(field,value=[2.5,['wrong','text',[]]])])),('Array element mismatch rejected',dict(request,controls=[dict(arrays[0],value=[True])],accessors=[])),('Multidimensional array rejected',dict(request,controls=[dict(arrays[0],value=[[1]])],accessors=[])),('Array of cluster rejected',dict(request,controls=[dict(arrays[0],element=field)],accessors=[])),('Complex replacement rejected',dict(child_req,overrides=[{'method':methods[0],'result_values':{'Settings':[3,[False,'x',[]]]}}]))]:
  prior=set((APP/'projects').iterdir()); result=client.rpc('tools/call',{'name':'create_type_artifact','arguments':bad});check(name,result.get('isError') is True and set((APP/'projects').iterdir())==prior)
 check('Original class/member bytes unchanged',all(hashed(p)==h for p,h in originals.items()))
 defaults={'name':'ComplexDefaults','kind':'class','controls':[{'kind':'cluster','label':'Defaults','children':[{'kind':'numeric','label':'Zero'},{'kind':'array','label':'Empty','element':{'kind':'string','label':'Element'}}]}]}
 default_result=client.call('create_type_artifact',defaults);write(folder/'defaults.json',default_result)
 check('Omitted nested defaults yield zero and empty array',default_result['native_private_fields'][0]['value']==[0,[]])
except Exception as e:failure=repr(e)
finally:client.close()
checks.append({'name':'NI session unchanged','passed':processes()==before})
report={'passed':failure is None and all(c['passed'] for c in checks),'failure':failure,'checks':checks,'folder':str(folder),'window_operations_used':False};write(folder/'acceptance.json',report)
print(json.dumps(report,ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
