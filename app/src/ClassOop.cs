using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
namespace OwnLabView {
// Only new owned classes are mutated. NI's wizard supplies the actual class
// terminals and dispatch rules; no NI library is changed or member flags forged.
internal static class ClassOop {
 internal static void Validate(Dictionary<string,object> doc){
  ClassInterfaces.Validate(doc);
  if(doc.ContainsKey("parent_class"))ParentReport(Data.Text(doc,"parent_class"));
  var names=new HashSet<string>(Data.Array(doc["controls"]).Select(Data.Map).Select(x=>Data.Text(x,"label")),StringComparer.Ordinal);
  if(doc.ContainsKey("accessors")){var access=Data.Array(doc["accessors"]);if(access.Length>32)throw new ArgumentException("At most32 own-field accessor pairs.");var selected=new HashSet<string>(StringComparer.Ordinal);foreach(var raw in access){var a=Data.Map(raw);Data.Keys(a,"field","dispatch","include_error_terminals");string field=Data.Text(a,"field");if(!names.Contains(field)||!selected.Add(field))throw new ArgumentException("Accessors require distinct fields declared in this class.");Dispatch(a);if(a.ContainsKey("include_error_terminals")&&!(a["include_error_terminals"] is bool))throw new ArgumentException("include_error_terminals must be Boolean.");}}
  if(doc.ContainsKey("overrides")){
   if(!doc.ContainsKey("parent_class")&&ClassInterfaces.Paths(doc).Length==0)throw new ArgumentException("Overrides require a class or interface parent.");
   var available=ClassInterfaces.Inherited(doc);
   var overrides=Data.Array(doc["overrides"]);if(overrides.Length>16)throw new ArgumentException("At most16 overrides.");
   var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var raw in overrides){
    var o=Data.Map(raw);Data.Keys(o,"method","result_values","source_path");string name=Storage.ArtifactFilename(o["method"],".vi");
    if(!selected.Add(name))throw new ArgumentException("Distinct override method filenames required.");
    var accessor=available.SingleOrDefault(v=>Path.GetFileName(Data.Text(v,"path")).Equals(name,StringComparison.OrdinalIgnoreCase));
    if(accessor==null)throw new ArgumentException("Override requires an actual dynamic accessor of the selected direct parent: "+name);
    if(accessor.ContainsKey("member_kind")&&Data.Text(accessor,"member_kind")=="custom"){
     if(!o.ContainsKey("source_path")||o.ContainsKey("result_values"))throw new ArgumentException("Custom overrides require source_path and do not use result_values.");
     GeneratedViDependency.ValidateOne(Data.Text(o,"source_path"));continue;
    }
    if(o.ContainsKey("source_path"))throw new ArgumentException("source_path is reserved for custom method overrides.");
    if(o.ContainsKey("result_values")){
     var values=Data.Map(o["result_values"]);string field=Data.Text(accessor,"field");
     if(values.Count>1||values.Count!=0&&(Data.Text(accessor,"direction")!="read"||!values.ContainsKey(field)))throw new ArgumentException("Accessor body replacement accepts only its actual read output field.");
     object current=accessor["default_value"];if(values.Count!=0 && current is object[])throw new ArgumentException("Complex accessor overrides currently forward the parent body; result_values requires a scalar output.");if(values.Count==0)continue;if(!(current is bool)&&!(current is string))current=Convert.ToDouble(current);
     foreach(var value in values){Recipe.NormalizeControlDefault(value.Value,current);if(value.Value is string&&((string)value.Value).Length>16384)throw new ArgumentException("Output String too long.");}
    }
   }
  }
 }
 internal static Dictionary<string,object>[] Catalog(Dictionary<string,object> report){
  report=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(report)));
  if(report.ContainsKey("dynamic_member_catalog"))return Data.Array(report["dynamic_member_catalog"]).Select(Data.Map).ToArray();
  var result=new List<Dictionary<string,object>>();var fields=Data.Array(report["native_private_fields"]).Select(Data.Map).ToDictionary(v=>Data.Text(v,"label"),StringComparer.Ordinal);
  if(report.ContainsKey("accessor_members"))foreach(var pair in Data.Array(report["accessor_members"]).Select(Data.Map).Where(v=>Data.Text(v,"dispatch")=="dynamic"))foreach(string direction in new[]{"read","write"})result.Add(new Dictionary<string,object>{{"path",pair[direction+"_path"]},{"sha256",pair[direction+"_sha256"]},{"field",pair["field"]},{"direction",direction},{"default_value",fields[Data.Text(pair,"field")]["value"]}});
  return result.ToArray();
 }
 internal static object[] BuildCatalog(Dictionary<string,object> doc,ClassSnapshot.Bundle parent,object[] accessors,object[] overrides,object[] fields,object[] custom,List<ClassSnapshot.Bundle> interfaces){
  var catalog=new Dictionary<string,Dictionary<string,object>>(StringComparer.OrdinalIgnoreCase);
  var bundles=new List<ClassSnapshot.Bundle>(interfaces);if(parent!=null)bundles.Insert(0,parent);
  foreach(var bundle in bundles)foreach(var original in Catalog(ParentReport(bundle.Source))){
   var row=new Dictionary<string,object>(original);string source=Data.Text(row,"path");if(!source.StartsWith(bundle.SourceRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Data.Hash(source)!=Data.Text(row,"sha256"))throw new IOException("Inherited dynamic method catalog identity differs.");
   row["path"]=Path.Combine(bundle.Root,source.Substring(bundle.SourceRoot.Length+1));if(row.ContainsKey("body_copy")){string body=Data.Text(row,"body_copy");if(!body.StartsWith(bundle.SourceRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Inherited method body is outside its bundle.");row["body_copy"]=Path.Combine(bundle.Root,body.Substring(bundle.SourceRoot.Length+1));}if(row.ContainsKey("body_snapshot_files")){row["body_snapshot_files"]=Data.Array(row["body_snapshot_files"]).Select(value=>{var file=new Dictionary<string,object>(Data.Map(value));string body=Data.Text(file,"path");if(!body.StartsWith(bundle.SourceRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Inherited body snapshot is outside its bundle.");file["path"]=Path.Combine(bundle.Root,body.Substring(bundle.SourceRoot.Length+1));return (object)file;}).ToArray();}catalog.Add(Path.GetFileName(source),row);
  }
  foreach(var value in overrides){var row=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(value)));string name=Path.GetFileName(Data.Text(row,"path"));if(row.ContainsKey("member_kind"))catalog[name]=row;else{catalog[name]["path"]=row["path"];catalog[name]["sha256"]=row["sha256"];}}
  foreach(var row in Catalog(new Dictionary<string,object>{{"accessor_members",accessors},{"native_private_fields",fields}})){string name=Path.GetFileName(Data.Text(row,"path"));if(catalog.ContainsKey(name))throw new ArgumentException("Own accessor name collides with an inherited dynamic member.");catalog.Add(name,row);}
  foreach(var raw in custom){var row=Data.Map(raw);if(Data.Text(row,"dispatch")!="dynamic")continue;string name=Path.GetFileName(Data.Text(row,"path"));if(catalog.ContainsKey(name))throw new ArgumentException("Custom member collides with inherited dynamic member; use overrides.");catalog.Add(name,row);}
  return catalog.Values.Cast<object>().ToArray();
 }
 static string Dispatch(Dictionary<string,object> a){string dispatch=a.ContainsKey("dispatch")?Data.Text(a,"dispatch"):"static";if(dispatch!="static"&&dispatch!="dynamic")throw new ArgumentException("dispatch must be static or dynamic.");return dispatch;}
 internal static Dictionary<string,object> ParentReport(string source){source=ViImport.CanonicalLocalPath(source);TypeImport.RejectVendorPath(source);string projects=Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar;if(!source.StartsWith(projects,StringComparison.OrdinalIgnoreCase)||!Path.GetExtension(source).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Parent requires a current app-authored .lvclass.");string report=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source)),"type-report.json");Storage.NoReparse(report);var d=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(report,Encoding.UTF8)));if(!d.ContainsKey("kind")||Data.Text(d,"kind")!="class"||Data.Text(d,"vi_path")!=source||Data.Text(d,"vi_sha256")!=Data.Hash(source))throw new IOException("Parent class authoring report/hash differs.");var bundle=ClassSnapshot.Plan(source);bundle.VerifySources();foreach(var method in Catalog(d)){string member=ViImport.CanonicalLocalPath(Data.Text(method,"path"));Storage.NoReparse(member);string expectedHash;if(!bundle.Sources.TryGetValue(member,out expectedHash)||!Path.GetExtension(member).Equals(".vi",StringComparison.OrdinalIgnoreCase)||expectedHash!=Data.Text(method,"sha256"))throw new IOException("Parent dynamic method is outside its pinned physical bundle or changed: "+member);}return d;}
 internal static ClassSnapshot.Bundle StageParent(Dictionary<string,object> doc,string directory){if(!doc.ContainsKey("parent_class"))return null;string source=Data.Text(doc,"parent_class");ParentReport(source);return ClassSnapshot.Copy(source,directory,"p");}
 internal static Dictionary<string,object> ParentInfo(object app,List<object> refs,string path,HashSet<string> seen,Dictionary<string,string> hashes){object probe=ClassArtifacts.OpenAuxiliary(app,refs,"OwnOop20261013_ParentIsNoneV3.vi",seen,hashes);LabView.Invoke(probe,"SetControlValue",BindingFlags.InvokeMethod,"Class Path",path);Run(probe);object none=LabView.Invoke(probe,"GetControlValue",BindingFlags.InvokeMethod,"No Parent");if(!(none is bool))throw new IOException("Native parent validity ABI differs.");if((bool)none)return new Dictionary<string,object>{{"path",""},{"name",""},{"no_parent",true}};object helper=ClassArtifacts.OpenAuxiliary(app,refs,"OwnOop20261013_ParentInfoV3.vi",seen,hashes);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Class Path",path);Run(helper);return new Dictionary<string,object>{{"path",Convert.ToString(LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"Parent Path"))},{"name",Convert.ToString(LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"Parent Name"))},{"no_parent",false}};}
 static void Set(object vi,Dictionary<string,object> args){foreach(var pair in args)LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,pair.Key,pair.Value);}
 static void Run(object helper){LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(helper);}
 static string ShortField(string name){string clean=new string(name.Where(c=>Char.IsLetterOrDigit(c)||c=='_').Take(16).ToArray());if(clean.Length==0)clean="Field";return clean+"_"+BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(name))).Replace("-","").Substring(0,6);}
 internal static object[] Accessors(object app,List<object> refs,string target,Dictionary<string,object> doc,HashSet<string> seen,Dictionary<string,string> hashes){
  if(!doc.ContainsKey("accessors"))return new object[0];var result=new List<object>();object library=LabView.Invoke(app,"OpenLibrary",BindingFlags.InvokeMethod,target);refs.Add(library);
  using(var n=new TypeImport.Session(app,refs))foreach(var spec in Data.Array(doc["accessors"]).Select(Data.Map)){
   string field=Data.Text(spec,"field");object reader=ClassArtifacts.OpenAuxiliary(app,refs,"Own20261013_ReadClassFields.vi",seen,hashes);Set(reader,new Dictionary<string,object>{{"Target Path",target}});Run(reader);uint[] controls=((IEnumerable)LabView.Invoke(reader,"GetControlValue",BindingFlags.InvokeMethod,"control_ids")).Cast<object>().Select(Convert.ToUInt32).ToArray();uint id=controls.Single(c=>Convert.ToString(n.Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",c}},"label")["label"])==field);
   string suffix=ShortField(field),read=Path.Combine(Path.GetDirectoryName(target),"Read_"+suffix+".vi"),write=Path.Combine(Path.GetDirectoryName(target),"Write_"+suffix+".vi");if(File.Exists(read)||File.Exists(write))throw new IOException("Accessor output exists.");object helper=ClassArtifacts.OpenAuxiliary(app,refs,"OwnOop20261013_CreateAccessorsDetached.vi",seen,hashes);bool error=spec.ContainsKey("include_error_terminals")&&(bool)spec["include_error_terminals"];string dispatch=Dispatch(spec);Set(helper,new Dictionary<string,object>{{"Class Path",target},{"Save Class Path",target},{"PDC Item Refnum",id},{"VI Type",(ushort)(dispatch=="dynamic"?0:1)},{"Access UI",(ushort)2},{"Include error terminals",error},{"Make available through Property Nodes",false},{"Virtual Folder Name",""},{"Read Path",read},{"Write Path",write}});Run(helper);
   foreach(string path in new[]{read,write}){object vi=n.Open(path);if(n.Type(vi)!=1||Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Accessor native type/state differs.");}result.Add(new{field,dispatch,read_path=read,write_path=write,read_sha256=Data.Hash(read),write_sha256=Data.Hash(write),include_error_terminals=error,generated_member_executed=false});
  }return result.ToArray();
 }
 internal static object[] Overrides(object app,List<object> refs,string target,Dictionary<string,object> doc,ClassSnapshot.Bundle parent,HashSet<string> seen,Dictionary<string,string> hashes){
  if(!doc.ContainsKey("overrides"))return new object[0];var inherited=ClassInterfaces.Inherited(doc);var allowed=inherited.Select(v=>Data.Text(v,"path")).ToArray();var result=new List<object>();
  using(var n=new TypeImport.Session(app,refs))foreach(var spec in Data.Array(doc["overrides"]).Select(Data.Map)){
   string filename=Storage.ArtifactFilename(spec["method"],".vi");
   if(spec.ContainsKey("source_path")){var expected=inherited.Single(v=>Path.GetFileName(Data.Text(v,"path")).Equals(filename,StringComparison.OrdinalIgnoreCase));result.Add(ClassMethods.Build(app,refs,target,filename,Data.Text(spec,"source_path"),"dynamic",expected,seen,hashes));continue;}
   string source=allowed.Single(p=>Path.GetFileName(p).Equals(filename,StringComparison.OrdinalIgnoreCase));string method=Path.Combine(parent.Root,source.Substring(parent.SourceRoot.Length+1));string output=Path.Combine(Path.GetDirectoryName(target),filename);if(File.Exists(output))throw new IOException("Override output collision.");object helper=ClassArtifacts.OpenAuxiliary(app,refs,"OwnOop20261013_CreateOverride.vi",seen,hashes);Set(helper,new Dictionary<string,object>{{"Parent Method Path",method},{"Child Class Path",target},{"Save Class Path",target},{"Memory Name",Path.GetFileName(target)+":"+filename},{"Member Path",output}});Run(helper);object vi=n.Open(output);
   if(spec.ContainsKey("result_values"))ReplaceScalarOutputs(app,refs,n,vi,output,Data.Map(spec["result_values"]),seen,hashes);
   if(n.Type(vi)!=1||Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Override is not a compiled normal VI.");result.Add(new{parent_method=method,path=output,sha256=Data.Hash(output),generated_member_executed=false,result_values=spec.ContainsKey("result_values")?spec["result_values"]:null});
  }return result.ToArray();
 }
 static uint[] Ids(object raw){return ((IEnumerable)raw).Cast<object>().Select(Convert.ToUInt32).ToArray();}
 static void ReplaceScalarOutputs(object app,List<object> refs,TypeImport.Session n,object vi,string path,Dictionary<string,object> values,HashSet<string> seen,Dictionary<string,string> hashes){
  uint diagram=Convert.ToUInt32(n.Call("Native_GetRootDiagram",new Dictionary<string,object>{{"target_path",path}},"diagram_id")["diagram_id"]);
  string inspectorPath=Path.Combine(Storage.Root,@"merge-helpers\OwnMergeWireTerms.vi");Storage.NoReparse(inspectorPath);
  var pin=Data.Map(Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,@"merge-helpers\inspection-provenance.json"))))["helpers"])["OwnMergeWireTerms"]);
  if(Data.Hash(inspectorPath)!=Data.Text(pin,"sha256"))throw new IOException("Override wire inspector changed.");hashes[inspectorPath]=Data.Hash(inspectorPath);object inspector=n.Open(inspectorPath);
  foreach(var pair in values){uint id=Ids(n.Call("Native_GetPanelControls",new Dictionary<string,object>{{"target_path",path}},"control_ids")["control_ids"]).Single(c=>Convert.ToString(n.Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",c}},"label")["label"])==pair.Key);string kind=Convert.ToString(n.Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name")["class_name"]);if(!new[]{"Digital","Boolean","String"}.Contains(kind)||!Convert.ToBoolean(n.Call("Own20261012_GetControlIndicator",new Dictionary<string,object>{{"control_id",id}},"is_indicator")["is_indicator"]))throw new ArgumentException("Override replacement requires an actual scalar output indicator.");object current=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,pair.Key);Recipe.NormalizeControlDefault(pair.Value,current);uint term=Convert.ToUInt32(n.Call("Native_GetControlTerminal",new Dictionary<string,object>{{"control_id",id}},"terminal_id")["terminal_id"]);
   foreach(uint wire in Ids(n.Call("Native_GetDiagramWires",new Dictionary<string,object>{{"diagram_id",diagram}},"wire_ids")["wire_ids"])){LabView.Invoke(inspector,"SetControlValue",BindingFlags.InvokeMethod,"wire_id",wire);LabView.Invoke(inspector,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(inspector);uint[] ends=Ids(LabView.Invoke(inspector,"GetControlValue",BindingFlags.InvokeMethod,"terminal_ids"));if(ends.Contains(term)){if(ends.Length!=2)throw new ArgumentException("Override replacement requires an unbranched scalar output wire.");n.Call("Native_DeleteGObject",new Dictionary<string,object>{{"object_id",wire}});}}
   var constant=n.Call("Own20261012_CreateConstantFromTerminal",new Dictionary<string,object>{{"terminal_id",term}},"constant_id");string setter=kind=="Digital"?"Own20261012_SetDoubleConstantValue":kind=="String"?"Native_SetStringConstantValue":null;if(setter==null){object writer=ClassArtifacts.OpenAuxiliary(app,refs,"OwnOop20261013_SetBooleanConstant.vi",seen,hashes);Set(writer,new Dictionary<string,object>{{"constant_id",constant["constant_id"]},{"value",pair.Value}});Run(writer);}else n.Call(setter,new Dictionary<string,object>{{"constant_id",constant["constant_id"]},{"value",pair.Value}});
  }LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,path,false,false);
 }
}
}
