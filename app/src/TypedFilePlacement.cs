using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OwnLabView {
// Called inside the recipe worker's NI lease. It opens only app-authored types
// accepted by GeneratedTypeDependency and acts on the recipe's new target.
public static class TypedFilePlacement {
    public static Dictionary<string,object> Place(object app,string target,GeneratedTypeDependency dependency,uint diagram,short x,short y,string globalName,bool write,List<object> refs,Dictionary<string,string> loadedPaths) {
        VerifyNative(app,dependency,refs);
        var helpers=new Dictionary<string,object>();
        foreach(string name in new[]{dependency.Kind=="control"?"Native_AddConstantFromTypedef":"Native_AddGlobalInDiagram","Native_GetObjectInfo","Own20261012_GetObjectIdentity","Native_GetTerminalTypeDescriptor","Native_GetTerminalName","Native_GetNodeTerminals"})Load(app,name,refs,helpers,loadedPaths);
        Dictionary<string,object> output;
        if(dependency.Kind=="control") {
            if(globalName!=null||write)throw new ArgumentException("CTL placement does not accept a global field or direction.");
            output=Call(helpers,"Native_AddConstantFromTypedef",new Dictionary<string,object>{{"owner_diagram_id",diagram},{"typedef_path",dependency.Path},{"x",x},{"y",y}},new[]{"constant_id","terminal_id"});
            uint constant=Convert.ToUInt32(output["constant_id"]),terminal=Convert.ToUInt32(output["terminal_id"]);if(constant==0||terminal==0)throw new InvalidOperationException("CTL placement returned a null object/terminal.");
            VerifyIdentity(helpers,target,constant);output["type_descriptor"]=Call(helpers,"Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",terminal}},new[]{"type_string"})["type_string"];
        } else {
            if(String.IsNullOrWhiteSpace(globalName))throw new ArgumentException("Global placement requires an exact field label.");
            // Use labels from the independently hashed report, not a mutable
            // request file, when choosing a field.
            var report=dependency.ReportData;
            var declared=Data.Array(report["controls"]).Select(Data.Map).Select(r=>Data.Text(r,"label")).ToArray();
            if(declared.Count(label=>String.Equals(label,globalName,StringComparison.Ordinal))!=1)throw new ArgumentException("Global field must match one exact label in the type report.");
            Load(app,"Native_SetGlobalControlName",refs,helpers,loadedPaths);Load(app,"Native_SetGlobalWrite",refs,helpers,loadedPaths);Load(app,"Native_GetGlobalControlName",refs,helpers,loadedPaths);Load(app,"Own20261012_GetTerminalContext",refs,helpers,loadedPaths);
            output=Call(helpers,"Native_AddGlobalInDiagram",new Dictionary<string,object>{{"target_path",target},{"owner_diagram_id",diagram},{"subvi_path",dependency.Path},{"x",x},{"y",y}},new[]{"object_id"});
            uint id=Convert.ToUInt32(output["object_id"]);VerifyIdentity(helpers,target,id);
            var info=Call(helpers,"Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},new[]{"class_name"});if(Convert.ToString(info["class_name"])!="Global")throw new InvalidOperationException("Placed global object has a different actual NI class.");
            Call(helpers,"Native_SetGlobalControlName",new Dictionary<string,object>{{"global_id",id},{"control_name",globalName}},new string[0]);Call(helpers,"Native_SetGlobalWrite",new Dictionary<string,object>{{"global_id",id},{"write",write}},new string[0]);
            var field=Call(helpers,"Native_GetGlobalControlName",new Dictionary<string,object>{{"global_id",id}},new[]{"control_name"});if(Convert.ToString(field["control_name"])!=globalName)throw new InvalidOperationException("Global field readback differs.");
            var terminals=Call(helpers,"Native_GetNodeTerminals",new Dictionary<string,object>{{"node_id",id}},new[]{"terminal_ids"});output["terminal_ids"]=terminals["terminal_ids"];
            var ids=((IEnumerable)output["terminal_ids"]).Cast<object>().Select(Convert.ToUInt32).ToArray();if(ids.Length!=1||ids[0]==0)throw new InvalidOperationException("Global scalar/cluster field should expose one terminal.");
            var context=Call(helpers,"Own20261012_GetTerminalContext",new Dictionary<string,object>{{"terminal_id",ids[0]}},new[]{"is_source"});if(!(context["is_source"] is bool)||(bool)context["is_source"]==write)throw new InvalidOperationException("Fresh global terminal direction differs from requested read/write mode.");
            output["terminal_id"]=ids[0];output["control_name"]=globalName;output["write"]=!(bool)context["is_source"];output["direction_verified"]=true;
            output["type_descriptor"]=Call(helpers,"Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",ids[0]}},new[]{"type_string"})["type_string"];
        }
        dependency.VerifyUnchanged();output["type_path"]=dependency.Path;output["type_sha256"]=dependency.Hash;return output;
    }
    public static void VerifyNative(object app,GeneratedTypeDependency dependency,List<object> refs) {
        dependency.VerifyUnchanged();object source=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,dependency.Path,"",false,0);refs.Add(source);
        if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(source,"Path",BindingFlags.GetProperty))),dependency.Path,StringComparison.OrdinalIgnoreCase)||Convert.ToInt32(LabView.Invoke(source,"VIType",BindingFlags.GetProperty))!=dependency.VIType)throw new InvalidOperationException("Loaded type artifact path or VIType differs from its snapshot.");
        if(((IEnumerable)LabView.Invoke(source,"_CalleesPaths",BindingFlags.GetProperty)).Cast<object>().Any(x=>!String.IsNullOrWhiteSpace(Convert.ToString(x))))throw new InvalidOperationException("Self-contained type artifact has unexpected actual callees.");dependency.VerifyUnchanged();
    }
    static void VerifyIdentity(Dictionary<string,object> helpers,string target,uint id) {
        if(id==0)throw new InvalidOperationException("Null placed object.");var info=Call(helpers,"Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},new[]{"uid","vi_path"});Recipe.ObjectIdentityKey(target,Convert.ToInt32(info["uid"]),Convert.ToString(info["vi_path"]));
    }
    static void Load(object app,string name,List<object> refs,Dictionary<string,object> helpers,Dictionary<string,string> loadedPaths) {
        if(helpers.ContainsKey(name))return;
        string metadataFile=Path.Combine(Storage.Root,"type-helpers","provenance.json");Storage.NoReparse(metadataFile);var entries=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(metadataFile,Encoding.UTF8)))["helpers"]);var entry=Data.Map(entries[name]);
        string path=Path.GetFullPath(Path.Combine(Storage.Root,Data.Text(entry,"relative_path")));Storage.NoReparse(path);if(!path.StartsWith(Storage.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Type helper escaped application.");string hash=Data.Text(entry,"sha256");if(Data.Hash(path)!=hash)throw new IOException("Type helper hash differs.");
        object helper=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(helper);
        if(!String.Equals(LabView.VerifyHelperPath(helper,hash),path,StringComparison.OrdinalIgnoreCase)||Convert.ToInt32(LabView.Invoke(helper,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Type helper path or executable state differs.");
        AuditNi(app,helper,refs,new HashSet<string>(StringComparer.OrdinalIgnoreCase));helpers[name]=helper;loadedPaths[name]=path;
    }
    static void AuditNi(object app,object vi,List<object> refs,HashSet<string> seen) {
        foreach(object raw in (IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)) {string path=Convert.ToString(raw);if(String.IsNullOrWhiteSpace(path))continue;path=Path.GetFullPath(path);if(!seen.Add(path))continue;if(seen.Count>256||!NiDistribution.AcceptDependency(path))throw new InvalidOperationException("Type helper dependency outside permitted NI vi.lib paths: "+path);Storage.NoReparse(path);
            object child=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(child);if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(child,"Path",BindingFlags.GetProperty))),path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("NI type-helper dependency resolved to a different path.");AuditNi(app,child,refs,seen);
        }
    }
    static Dictionary<string,object> Call(Dictionary<string,object> helpers,string name,Dictionary<string,object> args,string[] outputs) {
        object helper=helpers[name];foreach(var arg in args)LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,arg.Key,arg.Value);LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);var error=(IList)LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"error out");if(Convert.ToBoolean(error[0]))throw new InvalidOperationException(name+" NI error "+error[1]+": "+error[2]);
        var result=new Dictionary<string,object>();foreach(string output in outputs)result[output]=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,output);return result;
    }
}
}
