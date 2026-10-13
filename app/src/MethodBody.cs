using System;using System.Collections;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;
namespace OwnLabView {
// A method's own normal-VI dependencies travel with the class. Each node is
// relinked on a new copy; original VI files and loaded source diagrams are not edited.
internal static class MethodBody {
 internal sealed class Result {internal string Path;internal object[] Files;}
 static Dictionary<string,object> D(params object[] args){return ClassMethods.D(args);}
 static uint[] Ids(object a){return ((IEnumerable)a).Cast<object>().Select(Convert.ToUInt32).ToArray();}
 internal static Result Copy(object app,List<object> refs,TypeImport.Session n,string source,string directory,object[] closure,HashSet<string> seen,Dictionary<string,string> hashes){
  Directory.CreateDirectory(directory);var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{source,Data.Hash(source)}};
  foreach(var raw in closure){var row=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(raw)));string p=Data.Text(row,"path");if(!NiDistribution.AcceptDependency(p))sources[p]=Data.Text(row,"sha256");}
  var originals=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);var direct=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);long bytes=0;
  foreach(var pair in sources){if(!System.IO.Path.GetExtension(pair.Key).Equals(".vi",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Method closure currently requires own normal VIs or NI dependencies: "+pair.Key);GeneratedViDependency.ValidateOne(pair.Key).VerifyUnchanged();bytes+=new FileInfo(pair.Key).Length;if(bytes>268435456)throw new ArgumentException("Method closure exceeds256 MiB.");object vi=n.Open(pair.Key);if(n.Type(vi)!=1||Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new ArgumentException("Method dependency must be a compiled normal VI: "+pair.Key);originals[pair.Key]=vi;direct[pair.Key]=TypeImport.Session.Paths(vi);}
  var copies=sources.Keys.ToDictionary(p=>p,p=>System.IO.Path.Combine(directory,(p==source?"Body_":"Dependency_")+Guid.NewGuid().ToString("N")+".vi"),StringComparer.OrdinalIgnoreCase);
  foreach(var pair in copies)File.Copy(pair.Key,pair.Value,false);
  object relink=null;if(copies.Count>1)relink=ClassArtifacts.OpenAuxiliary(app,refs,"OwnMethod20261013_RelinkSubVI.vi",seen,hashes);
  var targets=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
  foreach(var pair in copies){object vi=n.Open(pair.Value);targets[pair.Key]=vi;uint root=Convert.ToUInt32(n.Call("Native_GetRootDiagram",D("target_path",pair.Value),"diagram_id")["diagram_id"]);var queue=new Queue<uint>();queue.Enqueue(root);var diagrams=new HashSet<uint>();
   while(queue.Count>0){uint diagram=queue.Dequeue();if(!diagrams.Add(diagram)||diagrams.Count>256)throw new IOException("Method diagram traversal exceeds its budget.");foreach(uint id in Ids(n.Call("Native_GetDiagramObjects",D("diagram_id",diagram),"object_ids")["object_ids"])){string kind=Convert.ToString(n.Call("Native_GetObjectInfo",D("object_id",id),"class_name")["class_name"]);
    if(kind=="SubVI"){string original=Convert.ToString(n.Call("Native_GetSubVIPath",D("subvi_id",id),"vi_path")["vi_path"]);string replacement;if(copies.TryGetValue(original,out replacement)){LabView.Invoke(relink,"SetControlValue",BindingFlags.InvokeMethod,"subvi_id",id);LabView.Invoke(relink,"SetControlValue",BindingFlags.InvokeMethod,"Path",replacement);LabView.Invoke(relink,"Run",BindingFlags.InvokeMethod,false);ClassArtifacts.CheckError(relink);}}
    else if(kind.EndsWith("Loop",StringComparison.Ordinal)||kind.EndsWith("Structure",StringComparison.Ordinal)||kind.EndsWith("Sequence",StringComparison.Ordinal))foreach(uint child in Ids(n.Call("Own20261012_GetStructureInfo",D("structure_id",id),"diagram_ids")["diagram_ids"]))queue.Enqueue(child);
   }}LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,pair.Value,false,false);
  }
  foreach(var pair in copies){object vi=targets[pair.Key];var expected=new HashSet<string>(direct[pair.Key].Select(p=>copies.ContainsKey(p)?copies[p]:p),StringComparer.OrdinalIgnoreCase);if(!expected.SetEquals(TypeImport.Session.Paths(vi))||Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Copied method dependency is missing, mismatched or not executable: "+pair.Value);if(Data.Hash(pair.Key)!=sources[pair.Key])throw new IOException("Original method closure changed.");}
  return new Result{Path=copies[source],Files=copies.Select(p=>(object)new{source_path=p.Key,source_sha256=sources[p.Key],path=p.Value,sha256=Data.Hash(p.Value)}).ToArray()};
 }
}
}
