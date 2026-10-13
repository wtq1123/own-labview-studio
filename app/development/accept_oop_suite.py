import copy,json,uuid
from pathlib import Path
from test_file_path_native import APP,Mcp,write,hashed,processes
folder=APP/'tests'/('oop-suite_'+uuid.uuid4().hex);folder.mkdir()
client=Mcp(folder);checks=[];failure=None;before=processes()
def check(name,ok):
 checks.append({'name':name,'passed':bool(ok)})
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'oop-suite','version':'1'}});client.notify_ready()
 request={'name':'OwnBase','kind':'class','controls':[{'kind':'numeric','label':'Gain','value':2.5},{'kind':'boolean','label':'Enabled','value':True},{'kind':'string','label':'Title','value':'自有类'},{'kind':'numeric','label':'Count','value':-2}],
          'accessors':[{'field':x,'dispatch':'dynamic' if x!='Count' else 'static','include_error_terminals':x=='Gain'} for x in ('Gain','Enabled','Title','Count')]}
 base=client.call('create_type_artifact',request);write(folder/'base.json',base);write(folder/'base-request.json',request)
 check('DBL Boolean String and static accessor pairs compiled',len(base['accessor_members'])==4 and base['private_field_names_types_defaults_checked'])
 parent_hashes={p:hashed(p) for p in [base['vi_path']]+[m[k] for m in base['accessor_members'] for k in ('read_path','write_path')]}
 methods={m['field']:Path(m['read_path']).name for m in base['accessor_members']}
 child_request={'name':'OwnChild','kind':'class','parent_class':base['vi_path'],'controls':[{'kind':'numeric','label':'Offset','value':1.5}],
                'accessors':[{'field':'Offset','dispatch':'dynamic'}],
                'overrides':[{'method':methods[field],'result_values':{field:value}} for field,value in [('Gain',42),('Enabled',False),('Title','子类')]]}
 child=client.call('create_type_artifact',child_request);write(folder/'child.json',child);write(folder/'child-request.json',child_request)
 check('Three scalar dynamic override bodies compiled',len(child['override_members'])==3)
 grand_request={'name':'OwnGrandchild','kind':'class','parent_class':child['vi_path'],'controls':[],
                'overrides':[{'method':methods[field],'result_values':{field:value}} for field,value in [('Gain',-7.25),('Enabled',True),('Title','孙类')]]}
 grand=client.call('create_type_artifact',grand_request);write(folder/'grandchild.json',grand);write(folder/'grandchild-request.json',grand_request)
 check('Ancestor dynamic accessors can be overridden by grandchild',len(grand['override_members'])==3 and len(grand['dynamic_member_catalog'])==8)
 prepared=client.call('prepare_file',{'source_path':grand['vi_path'],'name':'OwnClassMigrated'});write(folder/'prepared.json',prepared)
 check('Native parent hierarchy certified in independent snapshot',prepared['inheritance_closure_certified'] and not prepared['parent_metadata_unexposed'] and len([m for m in prepared['native_dependency_closure'] if m.get('edge')=='native_typed_library'])==2)
 for name,mutate in [('Static accessor cannot be overridden',lambda r:r['overrides'].__setitem__(0,{'method':methods['Count']})),('Output type mismatch rejected before authoring',lambda r:r['overrides'].__setitem__(0,{'method':methods['Gain'],'result_values':{'Gain':True}})),('Wrong output label rejected before authoring',lambda r:r['overrides'].__setitem__(0,{'method':methods['Gain'],'result_values':{'Wrong':9}})),('Accessor references declared own field only',lambda r:r.__setitem__('accessors',[{'field':'Missing','dispatch':'dynamic'}]))]:
  bad=copy.deepcopy(child_request);mutate(bad);projects=set((APP/'projects').iterdir());response=client.rpc('tools/call',{'name':'create_type_artifact','arguments':bad});check(name,response.get('isError') is True and set((APP/'projects').iterdir())==projects)
 check('All original base class/member bytes unchanged',all(hashed(p)==h for p,h in parent_hashes.items()))
except Exception as e:failure=repr(e)
finally:client.close()
check('NI session unchanged',processes()==before)
report={'passed':failure is None and all(c['passed'] for c in checks),'failure':failure,'checks':checks,'window_operations_used':False,'folder':str(folder)};write(folder/'acceptance.json',report)
print(json.dumps({'passed':report['passed'],'failure':failure,'folder':str(folder)},ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
