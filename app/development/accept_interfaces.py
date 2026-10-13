import copy,json,uuid
from pathlib import Path
from test_file_path_native import APP,Mcp,write,read,processes
folder=APP/'tests'/('interfaces_'+uuid.uuid4().hex);folder.mkdir();before=processes();client=Mcp(folder);checks=[];failure=None
def check(name,ok):
 checks.append(dict(name=name,passed=bool(ok)))
 if not ok:raise AssertionError(name)
try:
 client.rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'interface-acceptance','version':'1'}});client.notify_ready()
 recipe=read(APP/'recipes/connector-auto-sum.json');body=client.call('build_recipe',{'recipe':recipe});write(folder/'body.json',body)
 childrecipe=copy.deepcopy(recipe);childrecipe['name']='InterfaceMultiply'
 for step in childrecipe['steps']:
  if step['id']=='add':step['args']['style']='Multiply'
  if step['id']=='wo':step['args']['source_terminal']='$add.pins.x*y'
 multiply=client.call('build_recipe',{'recipe':childrecipe});write(folder/'multiply.json',multiply)
 root=client.call('create_type_artifact',dict(name='ICalculate',kind='class',is_interface=True,controls=[],methods=[dict(name='Compute',source_path=body['vi_path'])]));write(folder/'interface.json',root)
 check('NI confirms genuine interface and executable method',root['is_interface'] and root['native_class_metadata']['native_interfaces']['is_interface'])
 derived=client.call('create_type_artifact',dict(name='IDerived',kind='class',is_interface=True,controls=[],interfaces=[root['vi_path']]));write(folder/'derived.json',derived)
 check('Native interface inheritance',len(derived['native_class_metadata']['native_interfaces']['parent_interfaces'])==1)
 other=client.call('create_type_artifact',dict(name='IOther',kind='class',is_interface=True,controls=[],methods=[dict(name='Other',source_path=body['vi_path'])]));write(folder/'other.json',other)
 base=client.call('create_type_artifact',dict(name='InterfaceDefault',kind='class',controls=[],interfaces=[derived['vi_path'],other['vi_path']]));write(folder/'base.json',base)
 check('Class implements two interfaces',len(base['native_class_metadata']['native_interfaces']['parent_interfaces'])==2)
 child=client.call('create_type_artifact',dict(name='InterfaceOverride',kind='class',controls=[],parent_class=base['vi_path'],overrides=[dict(method='Compute.vi',source_path=multiply['vi_path'])]));write(folder/'child.json',child)
 check('Class child overrides inherited interface method',len(child['override_members'])==1)
 prepared=client.call('prepare_file',dict(name='InterfaceHierarchyCopy',source_path=child['vi_path']));write(folder/'prepared.json',prepared)
 check('Interface hierarchy relocates with unchanged sources',prepared['source_files_unchanged'] and prepared['inheritance_closure_certified'])
 for name,args in [('Private data on interface',dict(name='BadInterface',kind='class',is_interface=True,controls=[dict(kind='numeric',label='X')])),('Interface as class parent',dict(name='BadParent',kind='class',controls=[],parent_class=root['vi_path'])),('Class as interface',dict(name='BadKind',kind='class',controls=[],interfaces=[base['vi_path']]))]:
  prior=set((APP/'projects').iterdir());result=client.rpc('tools/call',{'name':'create_type_artifact','arguments':args});check(name+' rejected before authoring',result.get('isError') is True and prior==set((APP/'projects').iterdir()))
except Exception as e:failure=repr(e)
finally:client.close()
checks.append(dict(name='NI session unchanged',passed=before==processes()))
report=dict(passed=failure is None and all(c['passed'] for c in checks),failure=failure,checks=checks,folder=str(folder));write(folder/'acceptance.json',report);print(json.dumps(report,ensure_ascii=False));raise SystemExit(0 if report['passed'] else 1)
