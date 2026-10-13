using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OwnLabView {
// Finite file-object contract: two inspected NI candidates, never arbitrary
// external classes. Source/business code and generated target are never Run.
public static class NiFileObjects {
    sealed class Request {
        public string Name,Category,Filename,Source,SourceHash,ManifestPath,ManifestHash;
        public Dictionary<string,object> Manifest,Candidate;
        public short X,Y;
        public void Unchanged() {
            Storage.NoReparse(Source);Storage.NoReparse(ManifestPath);
            if(Data.Hash(Source)!=SourceHash||Data.Hash(ManifestPath)!=ManifestHash)throw new IOException("NI source or file-helper manifest changed.");
        }
    }
    static readonly Regex NamePattern=new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    static string Canonical(string value) {
        if(String.IsNullOrWhiteSpace(value)||value.Length<4||!Char.IsLetter(value[0])||value[1]!=':'||(value[2]!='\\'&&value[2]!='/')||value.Substring(2).IndexOf(':')>=0||value.Any(Char.IsControl))throw new ArgumentException("NI files require absolute local drive paths without streams.");
        foreach(string part in value.Substring(3).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries))if(part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("NI path has an ambiguous segment.");
        return Path.GetFullPath(value);
    }
    static short Coordinate(Dictionary<string,object> args,string key,short fallback) {
        if(!args.ContainsKey(key))return fallback;object value=args[key];
        if(!(value is int||value is long)||Convert.ToInt64(value)<Int16.MinValue||Convert.ToInt64(value)>Int16.MaxValue)throw new ArgumentException(key+" must be Int16.");return Convert.ToInt16(value);
    }
    static Request Parse(Dictionary<string,object> args) {
        Data.Keys(args,"name","category","source_path","filename","x","y");
        string name=Data.Text(args,"name"),category=Data.Text(args,"category");
        if(!NamePattern.IsMatch(name)||Regex.IsMatch(name,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z",RegexOptions.IgnoreCase))throw new ArgumentException("name requires a non-reserved ASCII identifier of at most40 characters.");
        if(category!="polymorphic"&&category!="class")throw new ArgumentException("category must be polymorphic or class.");
        string manifestPath=Path.Combine(Storage.Root,"file-helpers","manifest.json");Storage.NoReparse(manifestPath);
        if(!File.Exists(manifestPath)||new FileInfo(manifestPath).Length>1048576)throw new InvalidOperationException("Finite NI file manifest is unavailable.");
        string manifestHash=Data.Hash(manifestPath);var manifest=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(manifestPath,Encoding.UTF8)));
        if(!Object.Equals(manifest["native_development_placement_and_reopen_passed"],true))throw new InvalidOperationException("Finite NI placement evidence is absent.");
        var candidate=Data.Map(Data.Map(manifest["candidates"])[category]);string source=Canonical(Data.Text(candidate,"path"));
        foreach(var pair in Data.Map(manifest["helpers"])) {
            var helper=Data.Map(pair.Value);string path=Path.GetFullPath(Path.Combine(Storage.Root,Data.Text(helper,"relative_path")));string helperRoot=Path.GetFullPath(Path.Combine(Storage.Root,"file-helpers"))+Path.DirectorySeparatorChar;
            if(!path.StartsWith(helperRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("File helper escaped its manifest root.");Storage.NoReparse(path);
            if(!File.Exists(path)||Data.Hash(path)!=Data.Text(helper,"sha256"))throw new IOException("Pinned file helper changed: "+pair.Key);
        }
        if(args.ContainsKey("source_path")&&!String.Equals(Canonical(Data.Text(args,"source_path")),source,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("source_path must equal the one reviewed candidate for this category.");
        string registeredRoot=Path.Combine(Path.GetDirectoryName(LabView.RegisteredExecutable()),"vi.lib");
        if(!NiDistribution.AcceptDependency(source,registeredRoot))throw new ArgumentException("Reviewed candidate is outside the registered NI distribution.");
        Storage.NoReparse(source);string hash=Data.Text(candidate,"sha256");if(!File.Exists(source)||Data.Hash(source)!=hash)throw new IOException("Reviewed NI candidate bytes differ.");
        var request=new Request{Name=name,Category=category,Filename=args.ContainsKey("filename")?Storage.ArtifactFilename(args["filename"],".vi"):name+".vi",Source=source,SourceHash=hash,Manifest=manifest,Candidate=candidate,ManifestPath=manifestPath,ManifestHash=manifestHash,X=Coordinate(args,"x",220),Y=Coordinate(args,"y",120)};
        request.Unchanged();return request;
    }

    // Public filesystem-only preflight used by policy tests, not another MCP tool.
    public static object Validate(Dictionary<string,object> args) {
        var r=Parse(args);return new{valid=true,category=r.Category,source_path=r.Source,source_sha256=r.SourceHash,manifest_sha256=r.ManifestHash,labview_called=false};
    }
    static Request RecipeRequest(Dictionary<string,object> args) {
        string source=Canonical(Data.Text(args,"source_path"));string category;
        if(args.ContainsKey("category"))category=Data.Text(args,"category");
        else {
            var manifest=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"file-helpers","manifest.json"),Encoding.UTF8)));
            var matches=Data.Map(manifest["candidates"]).Where(pair=>String.Equals(Canonical(Data.Text(Data.Map(pair.Value),"path")),source,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)throw new ArgumentException("The source must have one reviewed file category.");category=matches[0].Key;
        }
        var request=new Dictionary<string,object>{{"name","RecipeFile"},{"category",category},{"source_path",source}};
        foreach(string key in new[]{"x","y"})if(args.ContainsKey(key))request[key]=args[key] is short?(object)Convert.ToInt32(args[key]):args[key];
        return Parse(request);
    }
    internal static object ValidateRecipeSource(Dictionary<string,object> args) {
        var r=RecipeRequest(args);return new{valid=true,source_path=r.Source,category=r.Category,source_sha256=r.SourceHash,manifest_sha256=r.ManifestHash,labview_called=false};
    }
    // Executes within the caller's existing native lease. References remain in
    // its collection until the complete recipe is saved/audited and disposed.
    internal static Dictionary<string,object> PlaceRecipe(object app,string target,Dictionary<string,object> args,List<object> references,Dictionary<string,string> loadedPaths) {
        var request=RecipeRequest(args);uint diagram=Convert.ToUInt32(args["owner_diagram_id"]);
        if(app==null||references==null||diagram==0)throw new ArgumentException("A live owned recipe diagram/session is required.");
        using(var context=new Context(request,app,references)) {
            object targetVi=context.Open(target);
            var owner=context.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",diagram}},"uid","vi_path");
            Recipe.ObjectIdentityKey(target,Convert.ToInt32(owner["uid"]),Convert.ToString(owner["vi_path"]));
            var before=context.MemberIdentities(target,diagram);
            Dictionary<string,object> placed;object type=null;object[] sourceClosure;
            if(request.Category=="polymorphic") {
                object source=context.Open(request.Source);
                if(Convert.ToInt32(LabView.Invoke(source,"VIType",BindingFlags.GetProperty))!=Convert.ToInt32(request.Candidate["native_vi_type"]))throw new InvalidOperationException("Native polymorphic VIType differs.");
                sourceClosure=context.Audit(Context.Paths(source));
                var output=context.Call("Native_AddSubVIInDiagram",new Dictionary<string,object>{{"target_path",target},{"owner_diagram_id",diagram},{"subvi_path",request.Source},{"x",request.X},{"y",request.Y}},"object_id","terminal_ids");
                placed=context.Object(target,Convert.ToUInt32(output["object_id"]),NativeIds(output["terminal_ids"]));
            } else {
                type=context.ClassType(request.Source);sourceClosure=new object[0];
                var output=context.Call("Native_AddConstantFromTypedef",new Dictionary<string,object>{{"owner_diagram_id",diagram},{"typedef_path",request.Source},{"x",request.X},{"y",request.Y}},"constant_id","terminal_id");
                placed=context.Object(target,Convert.ToUInt32(output["constant_id"]),new[]{Convert.ToUInt32(output["terminal_id"])});
            }
            if(Data.Text(placed,"actual_class")!=Data.Text(request.Candidate,"actual_object_class"))throw new InvalidOperationException("Placed native object class differs.");
            uint id=Convert.ToUInt32(placed["object_id"]);var after=context.MemberIdentities(target,diagram);string identity=Recipe.ObjectIdentityKey(target,Convert.ToInt32(placed["uid"]),Data.Text(placed,"module_vi_path"));
            if(before.Contains(identity,StringComparer.OrdinalIgnoreCase)||!after.Contains(identity,StringComparer.OrdinalIgnoreCase)||before.Except(after,StringComparer.OrdinalIgnoreCase).Any())throw new InvalidOperationException("Placement did not preserve the requested diagram's physical members.");
            placed["diagram_membership"]=new{before,after,added_identity=identity};
            var terminals=((IEnumerable)placed["terminals"]).Cast<object>().Select(Data.Map).ToArray();
            foreach(var terminal in terminals)if(Convert.ToUInt32(terminal["diagram_id"])!=diagram)throw new InvalidOperationException("A placed terminal is owned by a different diagram.");
            if(request.Category=="class")CheckKnownClassTerminal(terminals,request.Source);
            placed["terminal_ids"]=terminals.Select(row=>Convert.ToUInt32(row["terminal_id"])).ToArray();
            if(terminals.Length==1) {placed["terminal_id"]=terminals[0]["terminal_id"];placed["type_descriptor"]=terminals[0]["type_descriptor"];}
            placed["owner_diagram_id"]=diagram;placed["category"]=request.Category;placed["source_path"]=request.Source;placed["source_sha256"]=request.SourceHash;
            placed["source_dependency_closure"]=sourceClosure;placed["class_type_dependency"]=type;placed["source_files_unchanged"]=true;placed["source_saved"]=false;placed["source_executed"]=false;
            foreach(var helper in context.HelperPaths)loadedPaths["file:"+helper.Key]=helper.Value;
            placed["helper_paths"]=new Dictionary<string,string>(context.HelperPaths);request.Unchanged();GC.KeepAlive(targetVi);return placed;
        }
    }
    static void CheckKnownClassTerminal(Dictionary<string,object>[] terminals,string source) {
        // Source SHA/native library and member Paths are pinned separately.
        // Class descriptors contain process-local opaque words: the accepted
        // NI44972 and NI26068 reports differ there for identical source bytes.
        if(terminals.Length!=1)throw new InvalidOperationException("Known class must have one native terminal.");
        var terminal=terminals[0];int[] type=((IEnumerable)terminal["type_descriptor"]).Cast<object>().Select(Convert.ToInt32).ToArray();
        if(type.Length<2||(type[1]&255)!=0x70||!Convert.ToBoolean(terminal["is_source"])||Data.Text(terminal,"name")!=Path.GetFileName(source))throw new InvalidOperationException("Known class terminal family/name/direction differs.");
    }
    static uint[] NativeIds(object value) {var items=value as IEnumerable;if(items==null||value is string)throw new InvalidDataException("Native reference array required.");var ids=items.Cast<object>().Select(Convert.ToUInt32).ToArray();if(ids.Any(id=>id==0)||ids.Distinct().Count()!=ids.Length)throw new InvalidOperationException("Native reference array has null/duplicate references.");return ids;}
    internal static object AuditRecipeSources(object app,string target,IEnumerable<string> sourcePaths,IEnumerable<string> nativePaths,List<object> references,Dictionary<string,string> loadedPaths) {
        var sources=sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var rows=new List<object>();
        foreach(string source in sources) {
            var request=RecipeRequest(new Dictionary<string,object>{{"source_path",source}});
            using(var context=new Context(request,app,references)) {
                object vi=context.Open(target);var closure=context.Audit(nativePaths);
                object type=request.Category=="class"?context.ClassType(request.Source):null;
                rows.Add(new{source_path=request.Source,source_sha256=request.SourceHash,category=request.Category,native_dependency_closure=closure,class_type_dependency=type,source_files_unchanged=true,source_saved=false,source_executed=false});
                foreach(var helper in context.HelperPaths)loadedPaths["file:"+helper.Key]=helper.Value;request.Unchanged();GC.KeepAlive(vi);
            }
        }
        return rows.ToArray();
    }
    internal static object KnownClassMetadata(object app,string source,List<object> references) {
        var request=RecipeRequest(new Dictionary<string,object>{{"source_path",source},{"category","class"}});
        using(var context=new Context(request,app,references)){var result=context.ClassType(request.Source);request.Unchanged();return result;}
    }
    static void RunPhase(string requestPath,string phase,string folder) {
        var start=new ProcessStartInfo(Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--ni-file-worker \""+requestPath+"\" "+phase){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Storage.Root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        using(var child=new Process{StartInfo=start}) {
            child.Start();var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(180000)){child.Kill();child.WaitForExit(5000);throw new TimeoutException("NI file worker timed out; partial files and NI recovery record retained: "+folder);}
            if(child.ExitCode!=0)throw new InvalidOperationException("NI file "+phase+" failed: "+errors.Result+" Evidence: "+folder);
            if(!output.Wait(5000))throw new IOException("NI file worker output did not close.");
        }
    }
    public static object Place(Dictionary<string,object> args) {
        Request request=Parse(args);NiApplicationLifetime.EnsureForBuild();
        string folder=Path.GetFullPath(Path.Combine(Storage.Root,"projects",request.Name+"_NiFile_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")));
        if(!folder.StartsWith(Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("NI file project escaped output root.");
        Storage.NoReparse(folder);if(Directory.Exists(folder))throw new IOException("NI file output already exists.");Directory.CreateDirectory(folder);
        string requestPath=Path.Combine(folder,"ni-file-request.json");Storage.WriteNew(requestPath,args);
        RunPhase(requestPath,"build",folder);RunPhase(requestPath,"reopen",folder);
        var build=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(folder,"ni-file-build.json"),Encoding.UTF8)));
        var reopen=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(folder,"ni-file-reopen.json"),Encoding.UTF8)));
        var first=Data.Map(build["placed_object"]);var objects=Data.Array(reopen["objects"]).Select(Data.Map).Where(o=>Convert.ToInt32(o["uid"])==Convert.ToInt32(first["uid"])).ToArray();
        if(objects.Length!=1||Data.Text(objects[0],"actual_class")!=Data.Text(first,"actual_class"))throw new InvalidOperationException("Fresh reopened object identity/class differs.");
        var beforeTerms=Data.Array(first["terminals"]).Select(Data.Map).ToArray();var afterTerms=Data.Array(objects[0]["terminals"]).Select(Data.Map).ToArray();
        if(beforeTerms.Length!=afterTerms.Length)throw new InvalidOperationException("Fresh terminal count differs.");
        for(int i=0;i<beforeTerms.Length;i++)if(Data.Text(beforeTerms[i],"name")!=Data.Text(afterTerms[i],"name")||!Object.Equals(beforeTerms[i]["is_source"],afterTerms[i]["is_source"])||!Data.Array(beforeTerms[i]["type_descriptor"]).Select(Convert.ToInt32).SequenceEqual(Data.Array(afterTerms[i]["type_descriptor"]).Select(Convert.ToInt32)))throw new InvalidOperationException("Fresh terminal name/direction/type differs.");
        request.Unchanged();string target=Data.Text(reopen,"vi_path");if(Data.Hash(target)!=Data.Text(build,"vi_sha256")||!Object.Equals(reopen["static_checks_passed"],true))throw new IOException("Fresh target hash/static state differs.");
        var report=new{created=true,category=request.Category,vi_path=target,vi_sha256=Data.Hash(target),execution_state=1,static_checks_passed=true,actual_object=objects[0],fresh_worker_reopen_verified=true,source_path=request.Source,source_sha256=request.SourceHash,source_files_unchanged=true,source_saved=false,source_executed=false,generated_vi_executed=false,native_direct_callees=reopen["native_direct_callees"],native_dependency_closure=reopen["native_dependency_closure"],class_type_dependency=reopen["class_type_dependency"],helper_paths=reopen["helper_paths"],build_report_path=Path.Combine(folder,"ni-file-build.json"),reopen_report_path=Path.Combine(folder,"ni-file-reopen.json"),complete_replacement=false,limitations=new[]{"Only the two hashed NI candidates in file-helpers/manifest.json are accepted.","A class constant is serialized as a type and can be absent from direct VI callees.","Known class membership and descriptor are verified; the Library COM interface does not expose parent-class metadata. Unknown inherited classes remain excluded.","Source and generated VIs are never run; dynamic calls and external binary runtime behavior are not certified."}};
        Storage.WriteNew(Path.Combine(folder,"ni-file-report.json"),report);return report;
    }

    sealed class Context:IDisposable {
        public object App;
        readonly Request request;
        readonly List<object> refs=new List<object>();
        readonly Dictionary<string,object> helpers=new Dictionary<string,object>();
        public readonly Dictionary<string,string> HelperPaths=new Dictionary<string,string>();
        readonly Dictionary<string,object> helperEntries;
        readonly bool ownsApp=true,ownsReferences=true;
        public Context(Request value) {
            request=value;helperEntries=Data.Map(value.Manifest["helpers"]);
            App=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));
        }
        public Context(Request value,object application,List<object> references) {
            request=value;helperEntries=Data.Map(value.Manifest["helpers"]);App=application;refs=references;ownsApp=false;ownsReferences=false;
        }
        public object Open(string path) {
            object vi=LabView.Invoke(App,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(vi);
            if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Native module resolved to another path: "+path);return vi;
        }
        public Dictionary<string,object> Call(string name,Dictionary<string,object> args,params string[] outputs) {
            object helper;
            if(!helpers.TryGetValue(name,out helper)) {
                var entry=Data.Map(helperEntries[name]);string path=Path.GetFullPath(Path.Combine(Storage.Root,Data.Text(entry,"relative_path")));string root=Path.GetFullPath(Path.Combine(Storage.Root,"file-helpers"))+Path.DirectorySeparatorChar;
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("File helper escaped its root.");Storage.NoReparse(path);
                if(Data.Hash(path)!=Data.Text(entry,"sha256"))throw new IOException("File helper changed: "+name);
                helper=Open(path);if(Convert.ToInt32(LabView.Invoke(helper,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("File helper is not compiled idle: "+name);
                Audit(Paths(helper));helpers[name]=helper;HelperPaths[name]=path;
            }
            foreach(var pair in args) {
                if(pair.Key.EndsWith("_id",StringComparison.Ordinal)&&Convert.ToUInt32(pair.Value)==0)throw new ArgumentException("Null reference rejected before NI helper: "+pair.Key);
                LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,pair.Key,pair.Value);
            }
            LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);
            var error=(IList)LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"error out");if(Convert.ToBoolean(error[0]))throw new InvalidOperationException(name+" NI error "+error[1]+": "+error[2]);
            var result=new Dictionary<string,object>();foreach(string output in outputs)result[output]=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,output);return result;
        }
        public static string[] Paths(object vi) {return ((IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)).Cast<object>().Select(Convert.ToString).Where(p=>!String.IsNullOrWhiteSpace(p)).ToArray();}
        bool Allowed(string path) {
            if(!NiDistribution.AcceptDependency(path))return false;
            string full=Path.GetFullPath(path);
            return Data.Array(request.Manifest["reviewed_ni_roots"]).Select(Convert.ToString).Any(root=>full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||Data.Array(request.Manifest["reviewed_ni_files"]).Select(Convert.ToString).Any(file=>String.Equals(full,Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase));
        }
        static string Carrier(string path) {
            if(File.Exists(path))return path;string current=Path.GetDirectoryName(path);
            while(!String.IsNullOrEmpty(current)) {if(File.Exists(current)&&new[]{".vi",".vim",".lvclass",".llb"}.Contains(Path.GetExtension(current),StringComparer.OrdinalIgnoreCase))return current;current=Path.GetDirectoryName(current);}
            throw new FileNotFoundException("Native dependency has no real carrier.",path);
        }
        void VerifyCarrier(string path,string hash) {
            var accepted=Data.Map(request.Manifest["reviewed_carriers"]);object expected;
            if(!accepted.TryGetValue(path,out expected)||!String.Equals(Convert.ToString(expected),hash,StringComparison.OrdinalIgnoreCase))throw new IOException("NI carrier is not in the exact reviewed source snapshot: "+path);
        }
        public object[] Audit(IEnumerable<string> initial) {
            var queue=new Queue<string>(initial);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var rows=new List<object>();
            while(queue.Count>0) {
                string path=Path.GetFullPath(queue.Dequeue());if(!seen.Add(path))continue;if(seen.Count>256||!Allowed(path))throw new InvalidOperationException("Unreviewed NI dependency: "+path);Storage.NoReparse(path);string carrier=Carrier(path),hash=Data.Hash(carrier);VerifyCarrier(carrier,hash);
                if(Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)) {
                    if(!String.Equals(path,Data.Text(Data.Map(Data.Map(request.Manifest["candidates"])["class"]),"path"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Only the known NI class container is accepted.");
                    var type=ClassType(path);rows.Add(new{path,carrier,sha256=hash,edge="native_typed_library",type_metadata=type});
                } else if(Path.GetExtension(path).Equals(".lvlib",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unreviewed typed NI library container: "+path);
                else {
                    object vi=Open(path);string[] callees=Paths(vi);foreach(string callee in callees)queue.Enqueue(callee);
                    rows.Add(new{path,carrier,sha256=hash,edge="native_vi_callee",actual_path=Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty)),vi_type=Convert.ToInt32(LabView.Invoke(vi,"VIType",BindingFlags.GetProperty)),native_callees=callees});
                }
                if(Data.Hash(carrier)!=hash)throw new IOException("Native dependency carrier changed.");
            }
            return rows.ToArray();
        }
        public Dictionary<string,object> ClassType(string path) {
            var candidate=Data.Map(Data.Map(request.Manifest["candidates"])["class"]);
            if(!String.Equals(path,Data.Text(candidate,"path"),StringComparison.OrdinalIgnoreCase)||Data.Hash(path)!=Data.Text(candidate,"sha256"))throw new InvalidOperationException("Class is not the pinned NI candidate.");
            object library=LabView.Invoke(App,"OpenLibrary",BindingFlags.InvokeMethod,path);refs.Add(library);
            if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(library,"Path",BindingFlags.GetProperty))),path,StringComparison.OrdinalIgnoreCase)||Convert.ToString(LabView.Invoke(library,"TypeString",BindingFlags.GetProperty))!="LVClass")throw new InvalidOperationException("Typed class identity differs.");
            var declared=Data.Array(candidate["native_members"]).Select(Data.Map).ToArray();var members=new List<object>();var expected=new HashSet<string>(declared.Select(row=>Data.Text(row,"path")),StringComparer.OrdinalIgnoreCase);
            foreach(object item in (IEnumerable)LabView.Invoke(library,"Children",BindingFlags.GetProperty)) {
                refs.Add(item);string member=Path.GetFullPath(Convert.ToString(LabView.Invoke(item,"Path",BindingFlags.GetProperty))),kind=Convert.ToString(LabView.Invoke(item,"TypeString",BindingFlags.GetProperty));
                if(!expected.Remove(member)||!Allowed(member))throw new InvalidOperationException("Actual class membership differs from accepted evidence.");
                object vi;
                if(kind=="Class Private Data") {
                    if(!member.StartsWith(path+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Class private data resolves outside its real carrier.");
                    vi=LabView.Invoke(item,"VI",BindingFlags.GetProperty);refs.Add(vi);
                    if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))),member,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Embedded class data resolves elsewhere.");
                } else if(kind=="VI")vi=Open(member);
                else throw new InvalidOperationException("Unexpected known-class native member kind.");
                int type=Convert.ToInt32(LabView.Invoke(vi,"VIType",BindingFlags.GetProperty));string[] callees=Paths(vi);
                if(type!=(kind=="Class Private Data"?2:1)||callees.Length!=0)throw new InvalidOperationException("Known class member type or current native dependencies differ.");
                string carrier=kind=="Class Private Data"?path:member;
                VerifyCarrier(carrier,Data.Hash(carrier));
                members.Add(new{path=member,name=Convert.ToString(LabView.Invoke(item,"Name",BindingFlags.GetProperty)),type_string=kind,item_id=Convert.ToString(LabView.Invoke(item,"ItemID",BindingFlags.GetProperty)),actual_vi_path=member,vi_type=type,native_callees=callees,carrier,sha256=Data.Hash(carrier)});
            }
            if(expected.Count!=0)throw new InvalidOperationException("Native class members are missing.");
            return new Dictionary<string,object>{{"path",path},{"sha256",Data.Hash(path)},{"native_library_type","LVClass"},{"members",members.ToArray()},{"parent_metadata_unexposed",true},{"unknown_inherited_classes_supported",false}};
        }
        public Dictionary<string,object> Terminal(uint id) {
            var name=Call("Native_GetTerminalName",new Dictionary<string,object>{{"terminal_id",id}},"terminal_name");var type=Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",id}},"type_string");var context=Call("Own20261012_GetTerminalContext",new Dictionary<string,object>{{"terminal_id",id}},"is_source","diagram_id");
            int[] words=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();var bytes=words.SelectMany(word=>new[]{(byte)((word&65535)>>8),(byte)(word&255)}).ToArray();
            return new Dictionary<string,object>{{"terminal_id",id},{"name",Convert.ToString(name["terminal_name"])},{"type_descriptor",words},{"descriptor_bytes_big_endian_hex",BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant()},{"is_source",context["is_source"]},{"diagram_id",context["diagram_id"]}};
        }
        public string[] MemberIdentities(string target,uint diagram) {
            var identities=NativeIds(Call("Native_GetDiagramObjects",new Dictionary<string,object>{{"diagram_id",diagram}},"object_ids")["object_ids"]).Select(id=>{var identity=Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");return Recipe.ObjectIdentityKey(target,Convert.ToInt32(identity["uid"]),Convert.ToString(identity["vi_path"]));}).ToArray();
            if(identities.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=identities.Length)throw new InvalidOperationException("Duplicate physical Diagram member identity.");return identities;
        }
        public Dictionary<string,object> Object(string target,uint id,uint[] terms) {
            var info=Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name");var identity=Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");Recipe.ObjectIdentityKey(target,Convert.ToInt32(identity["uid"]),Convert.ToString(identity["vi_path"]));
            string kind=Convert.ToString(info["class_name"]);
            if(terms==null)terms=kind.Contains("Constant")?new[]{Convert.ToUInt32(Call("Native_GetConstantTerminal",new Dictionary<string,object>{{"constant_id",id}},"terminal_id")["terminal_id"])}:((IEnumerable)Call("Native_GetNodeTerminals",new Dictionary<string,object>{{"node_id",id}},"terminal_ids")["terminal_ids"]).Cast<object>().Select(Convert.ToUInt32).ToArray();
            return new Dictionary<string,object>{{"object_id",id},{"actual_class",kind},{"uid",identity["uid"]},{"module_vi_path",identity["vi_path"]},{"terminals",terms.Select(Terminal).ToArray()}};
        }
        public Dictionary<string,object> Snapshot(string target) {
            object vi=Open(target);uint diagram=Convert.ToUInt32(Call("Native_GetRootDiagram",new Dictionary<string,object>{{"target_path",target}},"diagram_id")["diagram_id"]);
            var objects=((IEnumerable)Call("Native_GetDiagramObjects",new Dictionary<string,object>{{"diagram_id",diagram}},"object_ids")["object_ids"]).Cast<object>().Select(Convert.ToUInt32).Select(id=>Object(target,id,null)).ToArray();
            var broken=((IEnumerable)Call("Native_GetDiagramWires",new Dictionary<string,object>{{"diagram_id",diagram}},"wire_ids")["wire_ids"]).Cast<object>().Select(Convert.ToUInt32).Where(id=>Convert.ToBoolean(Call("Native_GetWireBroken",new Dictionary<string,object>{{"wire_id",id}},"is_broken")["is_broken"])).ToArray();
            string[] callees=Paths(vi);int state=Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty));
            object classType=request.Category=="class"?ClassType(request.Source):null;
            return new Dictionary<string,object>{{"vi_path",target},{"vi_sha256",Data.Hash(target)},{"execution_state",state},{"static_checks_passed",state==1&&broken.Length==0},{"objects",objects},{"broken_wire_ids",broken},{"native_direct_callees",callees},{"native_dependency_closure",Audit(callees)},{"class_type_dependency",classType},{"helper_paths",HelperPaths}};
        }
        public void Dispose() {if(ownsReferences){for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);refs.Clear();}if(ownsApp)LabView.Release(App);App=null;}
    }

    public static object Worker(string requestPath,string phase) {
        if(phase!="build"&&phase!="reopen")throw new ArgumentException("Invalid NI file worker phase.");
        requestPath=Path.GetFullPath(requestPath);Storage.NoReparse(requestPath);
        if(!requestPath.StartsWith(Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(requestPath)!="ni-file-request.json"||new FileInfo(requestPath).Length>1048576)throw new UnauthorizedAccessException("NI file worker accepts bounded isolated requests only.");
        Request request=Parse(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(requestPath,Encoding.UTF8))));string folder=Path.GetDirectoryName(requestPath),target=Path.Combine(folder,request.Filename),reportPath=Path.Combine(folder,"ni-file-"+phase+".json");if(File.Exists(reportPath))throw new IOException("NI file worker report exists.");
        using(var lease=new NiSessionLease())using(var context=new Context(request)) {
            lease.Attached();
            try {
                Dictionary<string,object> placed=null;object vi=null;
                if(phase=="build") {
                    if(File.Exists(target))throw new IOException("New NI file target exists.");
                    context.Call("Native_Create",new Dictionary<string,object>{{"output_path",target}});vi=context.Open(target);
                    uint diagram=Convert.ToUInt32(context.Call("Native_GetRootDiagram",new Dictionary<string,object>{{"target_path",target}},"diagram_id")["diagram_id"]);
                    if(request.Category=="polymorphic") {
                        object source=context.Open(request.Source);if(Convert.ToInt32(LabView.Invoke(source,"VIType",BindingFlags.GetProperty))!=Convert.ToInt32(request.Candidate["native_vi_type"]))throw new InvalidOperationException("Native polymorphic VIType differs.");context.Audit(Context.Paths(source));
                        var output=context.Call("Native_AddSubVIInDiagram",new Dictionary<string,object>{{"target_path",target},{"owner_diagram_id",diagram},{"subvi_path",request.Source},{"x",request.X},{"y",request.Y}},"object_id","terminal_ids");
                        uint id=Convert.ToUInt32(output["object_id"]);uint[] ids=((IEnumerable)output["terminal_ids"]).Cast<object>().Select(Convert.ToUInt32).ToArray();placed=context.Object(target,id,ids);
                        foreach(var term in ((IEnumerable)placed["terminals"]).Cast<object>().Select(Data.Map))if(!Convert.ToBoolean(term["is_source"])&&!((int[])term["type_descriptor"]).SequenceEqual(new[]{4,0}))context.Call("Own20261012_CreateConstantFromTerminal",new Dictionary<string,object>{{"terminal_id",Convert.ToUInt32(term["terminal_id"])}},"constant_id","created_terminal_id");
                    } else {
                        context.ClassType(request.Source);
                        var output=context.Call("Native_AddConstantFromTypedef",new Dictionary<string,object>{{"owner_diagram_id",diagram},{"typedef_path",request.Source},{"x",request.X},{"y",request.Y}},"constant_id","terminal_id");placed=context.Object(target,Convert.ToUInt32(output["constant_id"]),new[]{Convert.ToUInt32(output["terminal_id"])});
                        CheckKnownClassTerminal(((IEnumerable)placed["terminals"]).Cast<object>().Select(Data.Map).ToArray(),request.Source);
                    }
                    if(Data.Text(placed,"actual_class")!=Data.Text(request.Candidate,"actual_object_class"))throw new InvalidOperationException("Placed object native class differs.");LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,target,false,false);
                } else {
                    var build=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(folder,"ni-file-build.json"),Encoding.UTF8)));if(Data.Hash(target)!=Data.Text(build,"vi_sha256"))throw new IOException("Target differs before fresh worker reopen.");
                }
                var snapshot=context.Snapshot(target);
                if(!Object.Equals(snapshot["static_checks_passed"],true))throw new InvalidOperationException("Saved NI file target is broken or uncompiled.");
                if(placed!=null) {
                    int uid=Convert.ToInt32(placed["uid"]);snapshot["placed_object"]=((IEnumerable)snapshot["objects"]).Cast<object>().Select(Data.Map).Single(o=>Convert.ToInt32(o["uid"])==uid);
                }
                request.Unchanged();snapshot["source_files_unchanged"]=true;snapshot["source_saved"]=false;snapshot["source_executed"]=false;snapshot["generated_vi_executed"]=false;snapshot["fresh_worker_reopen"]=phase=="reopen";snapshot["labview_version"]=Convert.ToString(LabView.Invoke(context.App,"Version",BindingFlags.GetProperty));Storage.WriteNew(reportPath,snapshot);return snapshot;
            } catch(Exception ex) {
                string failure=Path.Combine(folder,"ni-file-"+phase+"-failure.json");if(!File.Exists(failure))Storage.WriteNew(failure,new{created=false,phase,error=Data.Error(ex),target_path=target,source_path=request.Source,source_sha256_before=request.SourceHash,source_sha256_after=File.Exists(request.Source)?Data.Hash(request.Source):null,source_saved=false,source_executed=false,generated_vi_executed=false});throw;
            }
        }
    }
}
}
