using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OwnLabView {
// Migrated low-level operations. Sources are never Save/Run targets.
public static class BasicViTools {
    static string Source(string value) {
        string path=ViImport.CanonicalLocalPath(value);
        if(!File.Exists(path)||!path.EndsWith(".vi",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Existing local .vi required.");
        if(new FileInfo(path).Length>256L*1024*1024)throw new ArgumentException("VI exceeds 256 MiB.");
        return path;
    }
    internal static int Index(Dictionary<string,object> args,string key) {
        object value; if(!args.TryGetValue(key,out value)||!(value is int)||((int)value)<0||((int)value)>255)throw new ArgumentException(key+" must be an integer from 0 to 255.");
        return (int)value;
    }
    public static object Export(Dictionary<string,object> args) {
        Data.Keys(args,"path","output_html");string path=Source(Data.Text(args,"path"));
        string output=ViImport.CanonicalLocalPath(Data.Text(args,"output_html"));
        if(!output.EndsWith(".html",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("output_html must end in .html.");
        string folder=Path.GetDirectoryName(output);
        if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length!=0)throw new IOException("HTML and PNG export requires a new or empty directory.");
        string before=Data.Hash(path);object app=null,vi=null;
        try {
            using(var lease=new NiSessionLease()) {
                app=LabView.ActiveForInspection();lease.Attached();
                vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);
                Identity(vi,path);Directory.CreateDirectory(folder);
                LabView.Invoke(vi,"PrintVIToHTML",BindingFlags.InvokeMethod,output,false,4,0,24,folder);
                if(!File.Exists(output))throw new IOException("NI did not create HTML documentation.");
                if(Data.Hash(path)!=before)throw new IOException("Source bytes changed during documentation export.");
                return new {output_html=output,files=Directory.GetFiles(folder).OrderBy(x=>x).ToArray(),source_path=path,source_sha256=before,source_unchanged=true,source_saved=false,source_executed=false};
            }
        } finally {LabView.Release(vi);LabView.Release(app);}
    }
    public static object WireCopy(Dictionary<string,object> args) {
        Data.Keys(args,"source_vi","name","source_object","source_terminal","sink_object","sink_terminal");
        string source=Source(Data.Text(args,"source_vi"));string name=Data.Text(args,"name");
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z"))throw new ArgumentException("name requires 1..40 ASCII letters, digits or underscore, starting with a letter.");
        var graph=Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("AdditionDemo",name)));
        int si=Index(args,"source_terminal"),di=Index(args,"sink_terminal");
        string sl=Data.Text(args,"source_object"),dl=Data.Text(args,"sink_object");
        if(sl.Length>256||dl.Length>256)throw new ArgumentException("Object labels exceed 256 characters.");
        string folder=Storage.NewProject(graph),target=Path.Combine(folder,name+".vi"),request=Path.Combine(folder,"wire-request.json");
        Storage.WriteNew(request,new{source_vi=source,source_sha256=Data.Hash(source),target_vi=target,source_object=sl,source_terminal=si,sink_object=dl,sink_terminal=di});
        NiApplicationLifetime.EnsureForBuild();
        var start=new ProcessStartInfo(Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--wire-copy-worker \""+request+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using(var process=Process.Start(start)) {
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(120000)){process.Kill();process.WaitForExit();throw new TimeoutException("Wire-copy worker timed out. NI is left running; partial copy retained: "+folder);}
            if(process.ExitCode!=0)throw new InvalidOperationException(stderr.Result+" Partial copy retained: "+folder);
            return Data.Map(Data.Json.DeserializeObject(stdout.Result));
        }
    }
    static void Identity(object vi,string path) {
        if(!Path.GetFullPath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))).Equals(path,StringComparison.OrdinalIgnoreCase))throw new IOException("NI resolved a different VI path.");
    }
    static void AuditCallees(object app,object vi,HashSet<string> seen,int depth) {
        if(depth>32)throw new IOException("NI dependency closure is too deep.");
        string niRoot=Path.Combine(Path.GetDirectoryName(LabView.RegisteredExecutable()),"vi.lib");
        foreach(object raw in Items(LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty))) {
            string value=Convert.ToString(raw);if(String.IsNullOrWhiteSpace(value))continue;
            string path=ViImport.AllowedCallee(value,niRoot);if(!seen.Add(path))continue;
            if(seen.Count>256)throw new IOException("NI dependency closure exceeds 256 files.");
            object child=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);
            try{Identity(child,path);AuditCallees(app,child,seen,depth+1);}finally{LabView.Release(child);}
        }
    }
    sealed class Helpers:IDisposable {
        readonly object app; readonly Dictionary<string,object> loaded=new Dictionary<string,object>();
        readonly Dictionary<string,object> metadata=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","helper-provenance.json"),Encoding.UTF8)))["helpers"]);
        internal Helpers(object app){this.app=app;}
        internal Dictionary<string,object> Call(string name,Dictionary<string,object> values,params string[] outputs) {
            object vi;
            if(!loaded.TryGetValue(name,out vi)) {
                string path=Path.Combine(Storage.Root,"recipes","helpers",name+".vi");Storage.NoReparse(path);
                string hash=Data.Text(Data.Map(metadata[name]),"sha256");if(Data.Hash(path)!=hash)throw new IOException("Helper hash mismatch: "+name);
                vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);loaded.Add(name,vi);LabView.VerifyHelperPath(vi,hash);
                AuditCallees(app,vi,new HashSet<string>(StringComparer.OrdinalIgnoreCase),0);
            }
            foreach(var item in values)LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,item.Key,item.Value);
            LabView.Invoke(vi,"Run",BindingFlags.InvokeMethod,false);
            var error=((IEnumerable)LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"error out")).Cast<object>().ToArray();
            if(Convert.ToBoolean(error[0]))throw new InvalidOperationException(name+": "+Data.Json.Serialize(error));
            return outputs.ToDictionary(x=>x,x=>LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,x));
        }
        public void Dispose(){foreach(var vi in loaded.Values)LabView.Release(vi);}
    }
    static Dictionary<string,object> V(string key,object value){return new Dictionary<string,object>{{key,value}};}
    static object[] Items(object value){return ((IEnumerable)value).Cast<object>().ToArray();}
    internal static bool EqualTypes(int[] left,int[] right) {
        // Scalar TDs include the terminal label, which differs at each endpoint.
        // Complex/enum/fixed-point/reference/container domains keep exact payload equality.
        ViMerge.DescriptorSchema(left);ViMerge.DescriptorSchema(right);
        int code=left[1]&255;
        if(code!=(right[1]&255))return false;
        if(code>=1&&code<=11||code==0x21||code==0x30)return true;
        return left.SequenceEqual(right);
    }
    static uint Resolve(Helpers h,string target,uint root,string label,int index) {
        var matches=new List<uint[]>();
        foreach(object raw in Items(h.Call("Native_GetPanelControls",V("target_path",target),"control_ids")["control_ids"])) {
            uint control=Convert.ToUInt32(raw);
            if(Convert.ToString(h.Call("Native_GetControlLabel",V("control_id",control),"label")["label"])!=label)continue;
            matches.Add(new[]{Convert.ToUInt32(h.Call("Native_GetControlTerminal",V("control_id",control),"terminal_id")["terminal_id"])});
        }
        foreach(object raw in Items(h.Call("Native_GetDiagramObjects",V("diagram_id",root),"object_ids")["object_ids"])) {
            uint id=Convert.ToUInt32(raw);
            string kind=Convert.ToString(h.Call("Native_GetObjectInfo",V("object_id",id),"class_name")["class_name"]);
            // Front-panel terminals are resolved through their controls above.
            if(kind=="ControlTerminal"||kind=="Wire"||kind.EndsWith("Decoration",StringComparison.Ordinal))continue;
            Dictionary<string,object> text;
            try{text=h.Call("Own20261012_PublicGetNodeLabel_v1",V("node_id",id),"label");}
            catch(InvalidOperationException){continue;} // Read-only getter rejects non-Node objects.
            if(Convert.ToString(text["label"])==label)matches.Add(Items(h.Call("Native_GetNodeTerminals",V("node_id",id),"terminal_ids")["terminal_ids"]).Select(Convert.ToUInt32).ToArray());
        }
        if(matches.Count!=1)throw new ArgumentException("Require one exact top-level object label; found "+matches.Count+" for "+label);
        if(index>=matches[0].Length)throw new ArgumentException("Terminal index is out of range for "+label);
        return matches[0][index];
    }
    public static object Worker(string request) {
        request=ViImport.CanonicalLocalPath(request);
        if(!request.StartsWith(Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(request)!="wire-request.json")throw new UnauthorizedAccessException("Worker requires an owned request.");
        var args=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(request,Encoding.UTF8)));string source=Source(Data.Text(args,"source_vi"));
        string target=Path.Combine(Path.GetDirectoryName(request),Path.GetFileName(Data.Text(args,"target_vi")));Storage.NoReparse(target);
        if(!target.Equals(Data.Text(args,"target_vi"),StringComparison.OrdinalIgnoreCase)||File.Exists(target))throw new IOException("Invalid or existing copy destination.");
        string before=Data.Text(args,"source_sha256");if(Data.Hash(source)!=before)throw new IOException("Source changed since request creation.");
        object app=null,vi=null;
        try {
            using(var sourceLock=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var lease=new NiSessionLease()) {
                File.Copy(source,target,false);app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();
                vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,target,"",false,0);Identity(vi,target);
                AuditCallees(app,vi,new HashSet<string>(StringComparer.OrdinalIgnoreCase),0);
                using(var h=new Helpers(app)) {
                    uint root=Convert.ToUInt32(h.Call("Native_GetRootDiagram",V("target_path",target),"diagram_id")["diagram_id"]);
                    uint from=Resolve(h,target,root,Data.Text(args,"source_object"),Index(args,"source_terminal"));
                    uint to=Resolve(h,target,root,Data.Text(args,"sink_object"),Index(args,"sink_terminal"));
                    foreach(var endpoint in new[]{new{Id=from,Source=true},new{Id=to,Source=false}}) {
                        var context=h.Call("Own20261012_GetTerminalContext",V("terminal_id",endpoint.Id),"is_source","diagram_id");
                        var expected=h.Call("Own20261012_GetObjectIdentity",V("object_id",root),"uid","vi_path");
                        var actual=h.Call("Own20261012_GetObjectIdentity",V("object_id",Convert.ToUInt32(context["diagram_id"])),"uid","vi_path");
                        if(Convert.ToBoolean(context["is_source"])!=endpoint.Source||!Recipe.SameObjectIdentityData(target,Convert.ToInt32(expected["uid"]),Convert.ToString(expected["vi_path"]),Convert.ToInt32(actual["uid"]),Convert.ToString(actual["vi_path"])))throw new ArgumentException("Terminals must have source/sink direction in the same root diagram.");
                    }
                    if(Convert.ToUInt32(h.Call("Native_GetTerminalWire",V("terminal_id",to),"wire_id")["wire_id"])!=0)throw new ArgumentException("Sink terminal already has a wire.");
                    var fromType=Items(h.Call("Native_GetTerminalTypeDescriptor",V("terminal_id",from),"type_string")["type_string"]);
                    var toType=Items(h.Call("Native_GetTerminalTypeDescriptor",V("terminal_id",to),"type_string")["type_string"]);
                    if(!EqualTypes(fromType.Select(Convert.ToInt32).ToArray(),toType.Select(Convert.ToInt32).ToArray()))throw new ArgumentException("Terminal types differ; implicit coercion is not accepted.");
                    h.Call("Native_Wire",new Dictionary<string,object>{{"source_terminal",from},{"sink_terminal",to}});
                    uint wire=Convert.ToUInt32(h.Call("Native_GetTerminalWire",V("terminal_id",to),"wire_id")["wire_id"]);
                    if(wire==0||Convert.ToBoolean(h.Call("Native_GetWireBroken",V("wire_id",wire),"is_broken")["is_broken"]))throw new IOException("New wire is absent or broken.");
                }
                LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,target,false,false);
                if(Data.Hash(source)!=before)throw new IOException("Source changed during copy wiring.");
                var report=new{created=true,vi_path=target,vi_sha256=Data.Hash(target),source_vi=source,source_sha256=before,source_unchanged=true,source_saved=false,source_executed=false,generated_vi_executed=false,execution_state=Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty)),wire_verified=true,scope="One type-identical top-level connection in a new disk copy; compilation state is reported separately."};
                Storage.WriteNew(Path.Combine(Path.GetDirectoryName(request),"wire-copy-report.json"),report);return report;
            }
        } finally {LabView.Release(vi);LabView.Release(app);}
    }
}
}
