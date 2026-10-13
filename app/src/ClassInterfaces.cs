using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;
namespace OwnLabView {
internal static class ClassInterfaces {
 internal static void DefaultImplementation(object app,List<object> refs,string target,Dictionary<string,object> member,HashSet<string> seen,Dictionary<string,string> hashes){
  object library=LabView.Invoke(app,"OpenLibrary",BindingFlags.InvokeMethod,target);refs.Add(library);int index=0,found=-1;
  foreach(object item in (System.Collections.IEnumerable)LabView.Invoke(library,"Children",BindingFlags.GetProperty)){refs.Add(item);string path=TypeImport.NativePath(Convert.ToString(LabView.Invoke(item,"Path",BindingFlags.GetProperty)));if(String.Equals(path,Data.Text(member,"path"),StringComparison.OrdinalIgnoreCase))found=index;index++;}
  if(found<0)throw new IOException("Interface member not found in native owned items.");object helper=Helper(app,refs,"OwnInterface20261013_MustOverride.vi",seen,hashes,target);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Index",found);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Must Override",false);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Save Class Path",target);LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(helper);if(Convert.ToBoolean(LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"Actual")))throw new IOException("Interface default implementation flag did not persist.");member["must_override"]=false;member["native_must_override_checked"]=true;
 }
 internal static bool IsInterface(Dictionary<string,object> doc){return doc.ContainsKey("is_interface")&&(bool)doc["is_interface"];}
 internal static string[] Paths(Dictionary<string,object> doc){return doc.ContainsKey("interfaces")?Data.Array(doc["interfaces"]).Select(v=>{if(!(v is string))throw new ArgumentException("Interface paths must be strings.");return (string)v;}).ToArray():new string[0];}
 internal static void Validate(Dictionary<string,object> doc){
  if(doc.ContainsKey("is_interface")&&!(doc["is_interface"] is bool))throw new ArgumentException("is_interface must be Boolean.");
  if(IsInterface(doc)&&(Data.Array(doc["controls"]).Length!=0||doc.ContainsKey("parent_class")||doc.ContainsKey("accessors")))throw new ArgumentException("Interfaces have no private fields, class parent or field accessors; use interfaces for parent interfaces.");
  if(doc.ContainsKey("parent_class")&&IsInterface(ClassOop.ParentReport(Data.Text(doc,"parent_class"))))throw new ArgumentException("An interface belongs in interfaces, not parent_class.");
  string[] paths=Paths(doc);if(paths.Length>8||paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=paths.Length)throw new ArgumentException("At most8 distinct direct interfaces.");
  foreach(string path in paths)if(!IsInterface(ClassOop.ParentReport(path)))throw new ArgumentException("Only native interface artifacts can be implemented/inherited: "+path);
 }
 static object Helper(object app,List<object> refs,string name,HashSet<string> seen,Dictionary<string,string> hashes,string target){object vi=ClassArtifacts.OpenAuxiliary(app,refs,name,seen,hashes);LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,"Class Path",target);return vi;}
 internal static Dictionary<string,object> Info(object app,List<object> refs,string target,HashSet<string> seen,Dictionary<string,string> hashes){
  object vi=Helper(app,refs,"OwnInterface20261013_Info.vi",seen,hashes,target);LabView.Invoke(vi,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(vi);bool kind=Convert.ToBoolean(LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"Is Interface"));int count=Convert.ToInt32(LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"Count"));if(count<0||count>64)throw new IOException("Native direct interface count exceeds64.");
  var paths=new List<string>();if(count>0){vi=Helper(app,refs,"OwnInterface20261013_ParentPath.vi",seen,hashes,target);for(int i=0;i<count;i++){LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,"Index",i);LabView.Invoke(vi,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(vi);paths.Add(TypeImport.NativePath(Convert.ToString(LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"Parent Path"))));}}
  return new Dictionary<string,object>{{"is_interface",kind},{"parent_interfaces",paths.ToArray()}};
 }
 internal static List<ClassSnapshot.Bundle> Apply(object app,List<object> refs,string target,Dictionary<string,object> doc,HashSet<string> seen,Dictionary<string,string> hashes){
  var result=new List<ClassSnapshot.Bundle>();int index=0;foreach(string source in Paths(doc)){var bundle=ClassSnapshot.Copy(source,Path.Combine(Path.GetDirectoryName(target),"i"+(index++)),"p");object helper=Helper(app,refs,"OwnInterface20261013_AddParent.vi",seen,hashes,target);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Interface Path",bundle.Target);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,"Save Class Path",target);LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(helper);bundle.VerifySources();result.Add(bundle);}
  var actual=Info(app,refs,target,seen,hashes);if((bool)actual["is_interface"]!=IsInterface(doc)||!new HashSet<string>((string[])actual["parent_interfaces"],StringComparer.OrdinalIgnoreCase).SetEquals(result.Select(b=>b.Target)))throw new IOException("Native interface kind/parents differ from request.");return result;
 }
 internal static Dictionary<string,object>[] Inherited(Dictionary<string,object> doc){
  var paths=new List<string>(Paths(doc));if(doc.ContainsKey("parent_class"))paths.Insert(0,Data.Text(doc,"parent_class"));var result=paths.SelectMany(p=>ClassOop.Catalog(ClassOop.ParentReport(p))).ToArray();
  if(result.GroupBy(r=>Path.GetFileName(Data.Text(r,"path")),StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new ArgumentException("Inherited method filename is ambiguous across parents; use distinct method names.");return result;
 }
}
}
