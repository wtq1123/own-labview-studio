import copy,json,uuid
from pathlib import Path
from test_file_path_native import APP,Mcp,write,read,hashed,processes
folder=APP/'tests'/('custom-methods_'+uuid.uuid4().hex);folder.mkdir();before=processes();client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append({'name':name,'passed':bool(ok)})
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'custom-methods','version':'1'}});client.notify_ready()
 recipe=read(APP/'recipes/connector-auto-sum.json');basebody=client.call('build_recipe',{'recipe':recipe});write(folder/'basebody.json',basebody)
 childrecipe=copy.deepcopy(recipe);childrecipe['name']='CustomMultiplyBody'
 for step in childrecipe['steps']:
  if step['id']=='add':step['args']['style']='Multiply'
  if step['id']=='wo':step['args']['source_terminal']='$add.pins.x*y'
 childbody=client.call('build_recipe',{'recipe':childrecipe});write(folder/'childbody.json',childbody)
 base_req={'name':'CustomBase','kind':'class','controls':[],'methods':[{'name':'Compute','dispatch':'dynamic','source_path':basebody['vi_path']}]}
 write(folder/'base-request.json',base_req);base=client.call('create_type_artifact',base_req);write(folder/'base.json',base)
 check('Native custom method and both dynamic terminals created',len(base['custom_members'])==1 and len([r for r in base['custom_members'][0]['native_rules'] if r['rule']==4])==2)
 child_req={'name':'CustomChild','kind':'class','controls':[],'parent_class':base['vi_path'],'overrides':[{'method':'Compute.vi','source_path':childbody['vi_path']}]}
 write(folder/'child-request.json',child_req);child=client.call('create_type_artifact',child_req);write(folder/'child.json',child)
 check('Child custom body override compiled',len(child['override_members'])==1)
 prep=client.call('prepare_file',{'source_path':child['vi_path'],'name':'CustomHierarchyCopy'});write(folder/'prepared.json',prep)
 check('Custom class hierarchy including body copies migrated',prep['inheritance_closure_certified'] and prep['source_files_unchanged'])
 typed=read(APP/'recipes/array-defaults.json');typed['name']='TypedMethodBody'
 for key,style,label in [('flag','Horizontal Toggle Switch','Flag'),('text','String Control','Text')]:
  typed['steps'] += [{'id':key,'op':'control','args':{'style':style,'label':label,'x':50,'y':400}}, {'id':key+'echo','op':'typed_indicator','args':{'terminal_id':'$'+key+'.terminal_id'}},{'id':key+'label','op':'label','args':{'control_id':'$'+key+'echo.control_id','label':label+'Echo'}}]
 typed['steps'].append({'id':'connector','op':'connector_auto','args':{'control_labels':['Vector','VectorEcho','Matrix','MatrixEcho','Flag','FlagEcho','Text','TextEcho']}})
 typed['steps']=sorted(typed['steps'],key=lambda s:s['op'] in ('array_default','control_default'))
 typedbody=client.call('build_recipe',{'recipe':typed});write(folder/'typedbody.json',typedbody)
 typedclass=client.call('create_type_artifact',{'name':'TypedMethods','kind':'class','controls':[],'methods':[{'name':'Echo','source_path':typedbody['vi_path']},{'name':'StaticEcho','source_path':typedbody['vi_path'],'dispatch':'static'}]});write(folder/'typedclass.json',typedclass)
 typedprep=client.call('prepare_file',{'source_path':typedclass['vi_path'],'name':'TypedMethodsCopy'});write(folder/'typedprepared.json',typedprep)
 check('Bool String vector matrix static and dynamic methods compile and copy',len(typedclass['custom_members'])==2 and typedprep['source_files_unchanged'])
 bad=dict(child_req,overrides=[{'method':'Compute.vi','source_path':typedbody['vi_path']}]);answer=client.rpc('tools/call',{'name':'create_type_artifact','arguments':bad});check('Mismatched override signature rejected',answer.get('isError') is True)
 grand_req={'name':'CustomGrand','kind':'class','controls':[],'parent_class':child['vi_path'],'overrides':[{'method':'Compute.vi','source_path':basebody['vi_path']}]}
 grand=client.call('create_type_artifact',grand_req);write(folder/'grand.json',grand);check('Ancestor custom method can be overridden again',len(grand['override_members'])==1)
except Exception as e:failure=repr(e)
finally:client.close()
checks.append({'name':'NI process unchanged','passed':processes()==before})
report={'passed':failure is None and all(c['passed'] for c in checks),'failure':failure,'checks':checks,'folder':str(folder),'window_operations_used':False};write(folder/'acceptance.json',report);print(json.dumps(report,ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
