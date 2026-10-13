using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
namespace OwnLabView {
// NI's installed AddClass provider writes real private class data. This route
// authors independent root/child classes in isolated project contexts; no class blobs are synthesized.
public static class ClassArtifacts {
 public static object Worker(System.Collections.Generic.Dictionary<string,object> doc,string folder){
  object app=null,connection=null,project=null,helper=null;var refs=new List<object>();string phase="preflight";
  string directory=Path.Combine(folder,"class");Directory.CreateDirectory(directory);
  string target=Path.Combine(directory,doc.ContainsKey("filename")?Storage.ArtifactFilename(doc["filename"],".lvclass"):Data.Text(doc,"name")+"_"+Guid.NewGuid().ToString("N").Substring(0,8)+".lvclass");
  string helperPath=Path.Combine(Storage.Root,"class-helpers","Own20261013_CreateClass.vi"),manifestPath=Path.Combine(Storage.Root,"class-helpers","provenance.json");
  var sourceHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  object[] fields=Data.Array(doc["controls"]);string carrierPath=null,carrierHash=null;
  try {
   if(fields.Length>0){string carrierFolder=Path.Combine(folder,"field-carrier");Directory.CreateDirectory(carrierFolder);var carrierDoc=new Dictionary<string,object>{{"name","ClassFields"},{"kind","global"},{"controls",fields},{"filename","FieldCarrier.vi"}};string carrierRequest=Path.Combine(carrierFolder,"type-request.json");Storage.WriteNew(carrierRequest,carrierDoc);var carrierReport=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(TypeArtifacts.Worker(carrierRequest))));carrierPath=Data.Text(carrierReport,"vi_path");}
   Storage.NoReparse(target);if(File.Exists(target))throw new IOException("Class target exists.");var parentBundle=ClassOop.StageParent(doc,directory);
   var manifest=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(manifestPath,Encoding.UTF8)));string helperHash=Data.Text(manifest,"helper_sha256");
   Storage.NoReparse(helperPath);if(Data.Hash(helperPath)!=helperHash)throw new IOException("Class authoring helper changed.");
   using(var lease=new NiSessionLease()) {
    connection=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();
    phase="own_scratch_project";project=LabView.Invoke(connection,"NewProject",BindingFlags.InvokeMethod);LabView.Invoke(project,"Save",BindingFlags.InvokeMethod,Path.Combine(folder,"class-authoring.lvproj"));
    app=LabView.Invoke(project,"Application",BindingFlags.GetProperty);
    helper=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,helperPath,"",false,0);refs.Add(helper);
    if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(helper,"Path",BindingFlags.GetProperty))),helperPath,StringComparison.OrdinalIgnoreCase)||Convert.ToInt32(LabView.Invoke(helper,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Class helper actual path/state differs.");
    phase="audit_ni_provider";var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Audit(app,helper,refs,seen,sourceHashes);
    string provider=Data.Text(manifest,"provider_path");if(!sourceHashes.ContainsKey(provider)||sourceHashes[provider]!=Data.Text(manifest,"provider_sha256"))throw new IOException("Pinned NI AddClass provider differs.");
    if(parentBundle!=null){helper=OpenAuxiliary(app,refs,"OwnOop20261013_CreateChildV5.vi",seen,sourceHashes);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Parent Path",parentBundle.Target);}
    if(ClassInterfaces.IsInterface(doc))helper=OpenAuxiliary(app,refs,"OwnInterface20261013_Create.vi",seen,sourceHashes);
    phase="author_class";LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,ClassInterfaces.IsInterface(doc)?"Interface Path":"Class Path",target);LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);
    var error=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"error out") as System.Collections.IList;
    if(error==null||error.Count!=3||Convert.ToBoolean(error[0]))throw new InvalidOperationException("NI class authoring error: "+Data.Json.Serialize(error));
    if(!File.Exists(target)||Data.Hash(helperPath)!=helperHash)throw new IOException("Class not saved or helper changed.");object parentInfo=null;if(parentBundle!=null){parentInfo=ClassOop.ParentInfo(app,refs,target,seen,sourceHashes);if(!String.Equals(Data.Text((Dictionary<string,object>)parentInfo,"path"),parentBundle.Target,StringComparison.OrdinalIgnoreCase))throw new IOException("NI did not retain the requested copied parent.");parentBundle.VerifySources();}
    if(fields.Length>0){phase="author_private_fields";object fieldHelper=OpenAuxiliary(app,refs,"Own20261013_AddClassFields_RefsV3.vi",seen,sourceHashes);LabView.Invoke(fieldHelper,"SetControlValue",BindingFlags.InvokeMethod,"Target Path",target);LabView.Invoke(fieldHelper,"SetControlValue",BindingFlags.InvokeMethod,"Carrier Path",carrierPath);LabView.Invoke(fieldHelper,"Run",BindingFlags.InvokeMethod,false);CheckError(fieldHelper);}
    if(carrierPath!=null){carrierHash=Data.Hash(carrierPath);var carrierReport=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Path.GetDirectoryName(carrierPath),"type-report.json"),Encoding.UTF8)));if(carrierHash!=Data.Text(carrierReport,"vi_sha256"))throw new IOException("Private-field carrier file changed.");}
    phase="attach_interfaces";var interfaceBundles=ClassInterfaces.Apply(app,refs,target,doc,seen,sourceHashes);
    phase="attach_static_members";var memberCopies=AttachMembers(app,refs,target,directory,doc);
    phase="native_accessors";var accessorMembers=ClassOop.Accessors(app,refs,target,doc,seen,sourceHashes);
    phase="custom_members";var customMembers=ClassMethods.Create(app,refs,target,doc,seen,sourceHashes);
    phase="native_overrides";var overrideMembers=ClassOop.Overrides(app,refs,target,doc,parentBundle,seen,sourceHashes);
    phase="native_private_data_identity";var bundle=ClassSnapshot.Plan(target);bundle.Root=bundle.SourceRoot;foreach(var row in bundle.Sources)bundle.Copies.Add(row.Key,row.Value);
    object metadata;object[] nativeFields;using(var n=new TypeImport.Session(app,refs,bundle)){metadata=n.ClassMetadata(target);phase="verify_private_fields";nativeFields=ClassInterfaces.IsInterface(doc)?new object[0]:ReadFields(app,refs,n,target,fields,seen,sourceHashes);}
    foreach(var row in sourceHashes)if(Data.Hash(row.Key)!=row.Value)throw new IOException("NI dependency changed during authoring: "+row.Key);if(parentBundle!=null)parentBundle.VerifySources();
    var dynamicCatalog=ClassOop.BuildCatalog(doc,parentBundle,accessorMembers,overrideMembers,nativeFields,customMembers,interfaceBundles);
    var report=new{created=true,kind="class",is_interface=ClassInterfaces.IsInterface(doc),interface_snapshots=interfaceBundles.Select(b=>b.Rows()).ToArray(),vi_path=target,vi_sha256=Data.Hash(target),native_class_metadata=metadata,native_class_private_data_checked=!ClassInterfaces.IsInterface(doc),empty_root_class=fields.Length==0&&parentBundle==null,native_private_fields=nativeFields,private_field_names_types_defaults_checked=true,static_member_copies=memberCopies,field_carrier_path=carrierPath,field_carrier_sha256=carrierHash,accessor_members=accessorMembers,custom_members=customMembers,override_members=overrideMembers,dynamic_member_catalog=dynamicCatalog,native_parent=parentInfo,parent_snapshot=parentBundle==null?null:parentBundle.Rows(),helper_path=helperPath,helper_sha256=helperHash,ni_provider_sources=sourceHashes,generated_artifact_executed=false,generated_vi_executed=false,own_authoring_helper_executed=true,window_operations_used=false,complete_replacement=false,limitations=new[]{"Standalone root class with0..32 scalar, nested cluster or rectangular scalar/cluster array private fields and copied compiled standalone own public static members; copied own parents, native static/dynamic accessor pairs and native override scaffolds are supported; custom static/dynamic body wrappers and same-signature custom overrides are supported; native interfaces and copied normal-VI method closures are supported","prepare_file remains required before recipe placement; its snapshot checks are independent"}};
    Storage.WriteNew(Path.Combine(folder,"type-report.json"),report);return report;
   }
  }catch(Exception ex){Storage.WriteNew(Path.Combine(folder,"class-failure.json"),new{phase,error=Data.Error(ex),target,source_hashes=sourceHashes,window_operations_used=false});throw;}
  finally{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);LabView.Release(app);LabView.Release(project);LabView.Release(connection);}
 }
 static void Audit(object app,object vi,List<object> refs,HashSet<string> seen,Dictionary<string,string> hashes){
  string install=Path.GetFullPath(Path.GetDirectoryName(LabView.RegisteredExecutable()))+Path.DirectorySeparatorChar;
  foreach(object raw in (System.Collections.IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)){
   string path=Path.GetFullPath(Convert.ToString(raw));if(!seen.Add(path))continue;if(seen.Count>2048)throw new IOException("Class provider dependency budget exceeded.");
   TypeImport.RejectVendorPath(path);if(!path.StartsWith(install,StringComparison.OrdinalIgnoreCase)&&!OwnAuxiliaryPinned(path))throw new IOException("Class provider dependency outside installed NI or exact own helper: "+path);
   Storage.NoReparse(path);string carrier=TypeImport.PhysicalCarrier(path);hashes[carrier]=Data.Hash(carrier);
   // Native type-library carriers are pinned here. Class placement separately
   // inspects the resulting actual private data and static member closure.
   if(new[]{".lvclass",".lvlib"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))continue;
   object child=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(child);
   if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(child,"Path",BindingFlags.GetProperty))),path,StringComparison.OrdinalIgnoreCase))throw new IOException("NI provider callee resolved elsewhere.");Audit(app,child,refs,seen,hashes);
  }
 }
 static bool OwnAuxiliaryPinned(string path){if(!path.StartsWith(Storage.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return false;string manifestPath=Path.Combine(Storage.Root,"class-helpers","field-provenance.json");var manifest=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(manifestPath,Encoding.UTF8)));var files=Data.Map(manifest["files"]);string key=path.Substring(Storage.Root.Length+1);object hash;return files.TryGetValue(key,out hash)&&Data.Hash(path)==Convert.ToString(hash);}
 internal static object OpenAuxiliary(object app,List<object> refs,string name,HashSet<string> seen,Dictionary<string,string> hashes){string path=Path.Combine(Storage.Root,"class-helpers",name);Storage.NoReparse(path);if(!OwnAuxiliaryPinned(path))throw new IOException("Unpinned class helper.");object vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(vi);if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))),path,StringComparison.OrdinalIgnoreCase)||Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Class helper native identity/state differs.");hashes[path]=Data.Hash(path);Audit(app,vi,refs,seen,hashes);return vi;}
 internal static void CheckError(object vi){var error=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"error out") as System.Collections.IList;if(error==null||error.Count!=3||Convert.ToBoolean(error[0]))throw new InvalidOperationException("NI class field provider error: "+Data.Json.Serialize(error));}
 internal static object[] ReadFields(object app,List<object> refs,TypeImport.Session n,string path,object[] expected,HashSet<string> seen,Dictionary<string,string> hashes){
  object reader=OpenAuxiliary(app,refs,"Own20261013_ReadClassFields.vi",seen,hashes);LabView.Invoke(reader,"SetControlValue",BindingFlags.InvokeMethod,"Target Path",path);LabView.Invoke(reader,"Run",BindingFlags.InvokeMethod,false);CheckError(reader);
  var ids=((System.Collections.IEnumerable)LabView.Invoke(reader,"GetControlValue",BindingFlags.InvokeMethod,"control_ids")).Cast<object>().Select(Convert.ToUInt32).ToArray();if(ids.Length!=expected.Length)throw new IOException("Native class private field count differs.");
  if(ids.Length==0)return new object[0];
  var metadata=n.ClassMetadata(path);var privateRow=Data.Array(metadata["members"]).Select(v=>Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(v)))).Single(v=>Data.Text(v,"type_string")=="Class Private Data");string privatePath=Data.Text(privateRow,"path");object vi=n.Open(privatePath);
  var roots=((System.Collections.IEnumerable)n.Call("Native_GetPanelControls",new Dictionary<string,object>{{"target_path",privatePath}},"control_ids")["control_ids"]).Cast<object>().ToArray();if(roots.Length!=1)throw new IOException("Private data must have one native root cluster.");string label=Convert.ToString(n.Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",roots[0]}},"label")["label"]);object[] values=((System.Collections.IEnumerable)LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label)).Cast<object>().ToArray();if(values.Length!=ids.Length)throw new IOException("Native private data tuple size differs.");
  var byName=expected.Select(Data.Map).ToDictionary(x=>Data.Text(x,"label"),StringComparer.Ordinal);var result=new List<object>();
  for(int i=0;i<ids.Length;i++){string field=Convert.ToString(n.Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",ids[i]}},"label")["label"]);string kind=Convert.ToString(n.Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",ids[i]}},"class_name")["class_name"]);Dictionary<string,object> spec;if(!byName.TryGetValue(field,out spec))throw new IOException("Unexpected actual private field name: "+field);string requested=Data.Text(spec,"kind"),wanted=requested=="numeric"?"Digital":requested=="boolean"?"Boolean":requested=="cluster"?"Cluster":requested=="array"?"Array":"String";if(kind!=wanted)throw new IOException("Actual private field native class differs.");object template=FieldDefault(spec);object normalized=TypeArtifacts.NormalizeValue(spec,template,values[i]);if(!Recipe.ExactControlValueEqual(normalized,values[i]))throw new IOException("Actual private field default differs: "+field);result.Add(new{label=field,actual_class=kind,clr_type=values[i].GetType().FullName,value=values[i]});byName.Remove(field);}
  if(byName.Count!=0)throw new IOException("Missing native private field names.");return result.ToArray();
 }
 internal static object FieldDefault(Dictionary<string,object> spec){
  if(spec.ContainsKey("value"))return spec["value"];
  string kind=Data.Text(spec,"kind");
  if(kind=="array")return new object[0];
  if(kind=="cluster")return Data.Array(spec["children"]).Select(v=>FieldDefault(Data.Map(v))).ToArray();
  return kind=="numeric"?(object)0.0:kind=="boolean"?(object)false:"";
 }
 static object[] AttachMembers(object app,List<object> refs,string target,string directory,Dictionary<string,object> doc){
  if(!doc.ContainsKey("static_members"))return new object[0];var rows=new List<object>();object library=LabView.Invoke(app,"OpenLibrary",BindingFlags.InvokeMethod,target);refs.Add(library);
  using(var n=new TypeImport.Session(app,refs))foreach(var member in Data.Array(doc["static_members"]).Select(Data.Map)){
   var source=GeneratedViDependency.ValidateOne(Data.Text(member,"source_path"));source.VerifyUnchanged();object original=n.Open(source.Path);
   if(n.Type(original)!=1||Convert.ToInt32(LabView.Invoke(original,"ExecState",BindingFlags.GetProperty))!=1||TypeImport.Session.Paths(original).Length!=0)throw new ArgumentException("Static class members must be compiled standalone own normal VIs without subVI dependencies.");
   string path=Path.Combine(directory,Data.Text(member,"name")+".vi");Storage.NoReparse(path);File.Copy(source.Path,path,false);object item=LabView.Invoke(library,"AddFile",BindingFlags.InvokeMethod,path);refs.Add(item);object copy=n.Open(path);LabView.Invoke(copy,"SaveInstrument",BindingFlags.InvokeMethod,path,false,false);LabView.Invoke(library,"Save",BindingFlags.InvokeMethod,target);source.VerifyUnchanged();
   string privatePath=null;foreach(object child in (System.Collections.IEnumerable)LabView.Invoke(library,"Children",BindingFlags.GetProperty)){refs.Add(child);if(Convert.ToString(LabView.Invoke(child,"TypeString",BindingFlags.GetProperty))=="Class Private Data")privatePath=TypeImport.NativePath(Convert.ToString(LabView.Invoke(child,"Path",BindingFlags.GetProperty)));}
   if(privatePath==null||!privatePath.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Own native private-data member missing.");
   int type=n.Type(copy),state=Convert.ToInt32(LabView.Invoke(copy,"ExecState",BindingFlags.GetProperty));string[] callees=TypeImport.Session.Paths(copy);if(type!=1||state!=1||callees.Any(c=>!String.Equals(c,privatePath,StringComparison.OrdinalIgnoreCase)))throw new IOException("Saved static class member differs: type="+type+", state="+state+", callees="+Data.Json.Serialize(callees));rows.Add(new{source_path=source.Path,source_sha256=source.Sha256,path,sha256=Data.Hash(path),saved_callees=callees,own_private_data_path=privatePath,source_saved=false,source_executed=false,member_executed=false,execution_state=1});
  }return rows.ToArray();
 }
}
}
