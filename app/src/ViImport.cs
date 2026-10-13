using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OwnLabView {
// Import is a disk-copy transaction, never an edit of the explicitly supplied
// source. Static VI/typedef callees are restricted to registered NI vi.lib. This
// does not inspect dynamic VI calls, DLL calls, hardware or runtime effects.
public static class ViImport {
    const long MaxSourceBytes=256L*1024*1024;
    const int MaxRequestBytes=65536;
    static readonly Regex NamePattern=new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    static readonly Regex ReservedName=new Regex(@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z",RegexOptions.IgnoreCase);
    static readonly Regex HashPattern=new Regex(@"\A[0-9a-f]{64}\z");
    static readonly Regex TokenPattern=new Regex(@"\A[0-9a-f]{32}\z");

    internal sealed class Request {
        internal string Name,SourcePath,SourceHash,Token,Folder,Target,RequestPath;
        internal long SourceLength;
    }
    // Injected only by the isolated offline test assembly; the public Worker
    // always uses NativeBackend. No public option can substitute a backend.
    internal interface Backend:IDisposable {
        string NiRoot {get;}
        string Version {get;}
        object Open(string path);
        string LoadedPath(object vi);
        int State(object vi);
        string[] Callees(object vi);
        void Save(object vi,string path);
    }
    sealed class NativeBackend:Backend {
        readonly List<object> refs=new List<object>();
        NiSessionLease lease;
        object app;
        readonly string niRoot;
        public NativeBackend() {
            niRoot=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(LabView.RegisteredExecutable()),"vi.lib"));
            Storage.NoReparse(niRoot);
            if(!Directory.Exists(niRoot))throw new DirectoryNotFoundException("Registered NI vi.lib is unavailable: "+niRoot);
            try {
                lease=new NiSessionLease();
                app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));
                lease.Attached();
            }catch{Dispose();throw;}
        }
        public string NiRoot {get{return niRoot;}}
        public string Version {get{return Convert.ToString(LabView.Invoke(app,"Version",BindingFlags.GetProperty));}}
        public object Open(string path) {
            object vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);
            refs.Add(vi);return vi;
        }
        public string LoadedPath(object vi){return Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty));}
        public int State(object vi){return Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty));}
        public string[] Callees(object vi) {
            var result=new List<string>();
            object raw=LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty);
            var values=raw as IEnumerable;
            if(values==null||raw is string)throw new InvalidDataException("NI did not return a static VI callee list.");
            foreach(object value in values)result.Add(Convert.ToString(value));
            return result.ToArray();
        }
        public void Save(object vi,string path){LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,path,false,false);}
        public void Dispose() {
            try {for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);refs.Clear();LabView.Release(app);app=null;}
            finally {if(lease!=null){lease.Dispose();lease=null;}}
        }
    }

    static string ProjectsRoot {get{return Path.GetFullPath(Path.Combine(Storage.Root,"projects"));}}
    internal static string CanonicalLocalPath(string value) {
        if(String.IsNullOrWhiteSpace(value)||value.Length<4||!Char.IsLetter(value[0])||value[1]!=':'||(value[2]!='\\'&&value[2]!='/'))
            throw new ArgumentException("Import requires an absolute local drive path; UNC, relative and device paths are not supported.");
        if(value.Substring(2).Contains(":")||value.Any(Char.IsControl)||value.IndexOfAny(Path.GetInvalidPathChars())>=0)
            throw new ArgumentException("Alternate streams or invalid path characters are not supported.");
        foreach(string part in value.Substring(3).Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries))
            if(part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal)||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
                throw new ArgumentException("Import paths cannot contain traversal or ambiguous Windows segments.");
        string path=Path.GetFullPath(value);Storage.NoReparse(path);return path;
    }
    static string SourcePath(string value) {
        string path=CanonicalLocalPath(value);
        if(!Path.GetExtension(path).Equals(".vi",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Import source must be one .vi file.");
        if(!File.Exists(path))throw new FileNotFoundException("Import source is missing.",path);
        return path;
    }
    static string StreamHash(Stream stream) {
        stream.Position=0;
        using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    static FileStream OpenSource(Request request,bool compare) {
        string path=SourcePath(request.SourcePath);
        var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try {
            if(stream.Length<1||stream.Length>MaxSourceBytes)throw new InvalidDataException("Import supports nonempty VI files up to 256 MiB.");
            string hash=StreamHash(stream);
            if(compare&&(stream.Length!=request.SourceLength||hash!=request.SourceHash))throw new IOException("Source bytes changed after import preflight.");
            if(!compare){request.SourceLength=stream.Length;request.SourceHash=hash;}
            stream.Position=0;return stream;
        }catch{stream.Dispose();throw;}
    }
    internal static Request Parse(Dictionary<string,object> args) {
        if(args==null)throw new ArgumentNullException("args");
        Data.Keys(args,"source_path","name");
        string name=Data.Text(args,"name");
        if(!NamePattern.IsMatch(name)||ReservedName.IsMatch(name))throw new ArgumentException("Import name must be a non-reserved ASCII identifier of at most 40 characters.");
        var request=new Request{Name=name,SourcePath=SourcePath(Data.Text(args,"source_path"))};
        using(OpenSource(request,false)){}
        return request;
    }
    public static object Validate(Dictionary<string,object> args) {
        Request request=Parse(args);
        return new {valid=true,name=request.Name,source_path=request.SourcePath,source_sha256=request.SourceHash,source_bytes=request.SourceLength,
            labview_called=false,vi_format_verified=false,dependencies_verified=false,
            scope="Filesystem preflight only. Native acceptance is restricted to compiled idle copies whose static VI callees recursively stay in registered NI vi.lib. Dynamic/DLL/runtime dependencies are not certified."};
    }
    internal static Request Prepare(Request request) {
        request.Token=Guid.NewGuid().ToString("N");
        request.Folder=Path.Combine(ProjectsRoot,request.Name+"_Import_"+request.Token);
        request.Target=Path.Combine(request.Folder,Path.GetFileName(request.Folder)+".vi");
        request.RequestPath=Path.Combine(request.Folder,"import-request.json");
        Storage.NoReparse(request.Folder);
        if(Directory.Exists(request.Folder)||File.Exists(request.Folder))throw new IOException("Unique import output already exists.");
        Directory.CreateDirectory(request.Folder);Storage.NoReparse(request.Folder);
        Storage.WriteNew(request.RequestPath,new{schema_version=1,name=request.Name,source_path=request.SourcePath,source_sha256=request.SourceHash,source_bytes=request.SourceLength,token=request.Token,output_path=request.Target});
        return request;
    }
    internal static Request ReadRequest(string path) {
        path=CanonicalLocalPath(path);
        string prefix=ProjectsRoot+Path.DirectorySeparatorChar;
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="import-request.json")throw new UnauthorizedAccessException("Import worker requires an isolated import-request.json under projects.");
        string json;
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            if(stream.Length<1||stream.Length>MaxRequestBytes)throw new InvalidDataException("Import worker request exceeds the supported size.");
            using(var reader=new StreamReader(stream,Encoding.UTF8,true))json=reader.ReadToEnd();
        }
        var raw=Data.Map(Data.Json.DeserializeObject(json));
        Data.Keys(raw,"schema_version","name","source_path","source_sha256","source_bytes","token","output_path");
        object version,bytes;
        if(!raw.TryGetValue("schema_version",out version)||!(version is int)||(int)version!=1)throw new ArgumentException("Invalid import request version.");
        string name=Data.Text(raw,"name"),token=Data.Text(raw,"token"),hash=Data.Text(raw,"source_sha256");
        if(!NamePattern.IsMatch(name)||ReservedName.IsMatch(name)||!TokenPattern.IsMatch(token)||!HashPattern.IsMatch(hash))throw new ArgumentException("Invalid import request identity.");
        if(!raw.TryGetValue("source_bytes",out bytes)||!(bytes is int||bytes is long))throw new ArgumentException("Invalid source length.");
        long length=Convert.ToInt64(bytes);
        if(length<1||length>MaxSourceBytes)throw new ArgumentException("Invalid source length.");
        string folder=Path.Combine(ProjectsRoot,name+"_Import_"+token);
        string target=Path.Combine(folder,Path.GetFileName(folder)+".vi");
        if(!String.Equals(Path.GetDirectoryName(path),folder,StringComparison.OrdinalIgnoreCase)||!String.Equals(CanonicalLocalPath(Data.Text(raw,"output_path")),target,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Import output and worker request identities differ.");
        var request=new Request{Name=name,Token=token,SourcePath=SourcePath(Data.Text(raw,"source_path")),SourceHash=hash,SourceLength=length,Folder=folder,Target=target,RequestPath=path};
        if(String.Equals(request.SourcePath,target,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Import source cannot be its output.");
        return request;
    }
    public static object Import(Dictionary<string,object> args) {
        Request request=Prepare(Parse(args));
        // Pin both files before NI activation. The worker can read these handles,
        // but Windows denies concurrent write/delete through this transaction.
        using(var requestLock=new FileStream(request.RequestPath,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var sourceLock=OpenSource(request,true)) {
        SameRequest(request,ReadRequest(request.RequestPath));
        // Explicit Import may start NI. Validate and Parse remain filesystem-only.
        NiApplicationLifetime.EnsureForBuild();
        var start=new ProcessStartInfo(Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--import-worker \""+request.RequestPath+"\"") {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Storage.Root,
            RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        using(var child=new Process{StartInfo=start}) {
            child.Start();var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(180000)) {
                try{child.Kill();child.WaitForExit(5000);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
                throw new TimeoutException("Import worker timed out; worker termination attempted. Partial copy and NI recovery record are retained: "+request.Folder);
            }
            if(!output.Wait(5000)||!errors.Wait(5000))throw new IOException("Import worker did not close its output pipes.");
            if(child.ExitCode!=0)throw new InvalidOperationException("Import failed; partial copy retained: "+request.Folder+". "+errors.Result);
        }
        var accepted=GeneratedViDependency.ValidateOne(request.Target);
        var report=accepted.ReportData;
        if(!report.ContainsKey("artifact_kind")||!Object.Equals(report["artifact_kind"],"imported_vi"))throw new IOException("Worker did not return a successful import report.");
        var origin=Data.Map(report["import_source"]);
        if(!String.Equals(Data.Text(origin,"path"),request.SourcePath,StringComparison.OrdinalIgnoreCase)||Data.Text(origin,"before_sha256")!=request.SourceHash)
            throw new IOException("Worker report identifies a different import source.");
        using(OpenSource(request,true)){}
        return report;
        }
    }
    internal static void SameRequest(Request expected,Request actual) {
        if(!String.Equals(expected.SourcePath,actual.SourcePath,StringComparison.OrdinalIgnoreCase)||expected.SourceHash!=actual.SourceHash||expected.SourceLength!=actual.SourceLength||!String.Equals(expected.Target,actual.Target,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Import source or output identity changed before worker dispatch.");
    }
    public static object Worker(string requestPath) {return RunRequest(requestPath,()=>new NativeBackend());}
    internal static string AllowedCallee(string value,string root) {
        string path=CanonicalLocalPath(value);
        string prefix=CanonicalLocalPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string extension=Path.GetExtension(path);
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!(extension.Equals(".vi",StringComparison.OrdinalIgnoreCase)||extension.Equals(".ctl",StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Import accepts only static VI/CTL dependencies inside registered NI vi.lib: "+path);
        if(!NiDistribution.AcceptDependency(path,root))
            throw new UnauthorizedAccessException("Import refuses a vendor or legacy dependency even inside vi.lib: "+path);
        if(!File.Exists(path)) {
            // LLB members are virtual files. Require an actual .llb ancestor;
            // a missing regular VI cannot pass merely because it has a NI prefix.
            string container=Path.GetDirectoryName(path);
            while(container!=null&&!File.Exists(container))container=Path.GetDirectoryName(container);
            if(container==null||!Path.GetExtension(container).Equals(".llb",StringComparison.OrdinalIgnoreCase))throw new FileNotFoundException("NI callee or its LLB container does not exist.",path);
        }
        return path;
    }
    static void Identity(Backend backend,object vi,string expected) {
        string actual=CanonicalLocalPath(backend.LoadedPath(vi));
        if(!String.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("NI loaded a different VI path: "+actual+"; expected "+expected);
    }
    static string[] Audit(Backend backend,object vi,HashSet<string> visited,int depth) {
        if(depth>64)throw new InvalidDataException("Import NI callee nesting exceeds 64.");
        string[] raw=backend.Callees(vi);
        if(raw==null)throw new InvalidDataException("Missing NI static callee list.");
        var direct=new List<string>();
        foreach(string value in raw) {
            if(String.IsNullOrWhiteSpace(value))throw new InvalidDataException("Unresolved or blank static VI callee.");
            string path=AllowedCallee(value,backend.NiRoot);direct.Add(path);
            if(!visited.Add(path))continue;
            if(visited.Count>256)throw new InvalidDataException("Import static VI closure exceeds 256 paths.");
            object child=backend.Open(path);Identity(backend,child,path);Audit(backend,child,visited,depth+1);
        }
        return direct.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static object RunRequest(string requestPath,Func<Backend> createBackend) {
        Request request=ReadRequest(requestPath);
        string reportPath=Path.Combine(request.Folder,"build-report.json");
        if(File.Exists(request.Target)||File.Exists(reportPath)||File.Exists(Path.Combine(request.Folder,"import-failure.json")))throw new IOException("Import output already exists; never overwrite or resume a partial import.");
        using(var source=OpenSource(request,true)) {
            try {
                Storage.NoReparse(request.Target);
                using(var output=new FileStream(request.Target,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(output);
                if(Data.Hash(request.Target)!=request.SourceHash)throw new IOException("Copied VI differs from captured source bytes.");
                object report;string targetHash;
                using(Backend backend=createBackend()) {
                    object vi=backend.Open(request.Target);Identity(backend,vi,request.Target);
                    if(backend.State(vi)!=1)throw new InvalidOperationException("Imported copy is not in compiled idle state 1; not saved as accepted.");
                    var before=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Audit(backend,vi,before,0);
                    Storage.NoReparse(request.Target);backend.Save(vi,request.Target);Identity(backend,vi,request.Target);
                    int state=backend.State(vi);
                    if(state!=1)throw new InvalidOperationException("Saved imported copy is not in compiled idle state 1.");
                    var after=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string[] direct=Audit(backend,vi,after,0);
                    Storage.NoReparse(request.SourcePath);Storage.NoReparse(request.Target);
                    string sourceAfter=StreamHash(source);
                    if(sourceAfter!=request.SourceHash||Data.Hash(request.SourcePath)!=request.SourceHash)throw new IOException("Source changed during import.");
                    targetHash=Data.Hash(request.Target);
                    report=new {created=true,artifact_kind="imported_vi",vi_path=request.Target,vi_sha256=targetHash,
                        labview_version=backend.Version,execution_state=state,static_checks_passed=true,static_acceptance_basis="Saved VI ExecState=1 and recursively allowed static VI/typedef callee paths; no diagram or runtime test",
                        generated_vi_executed=false,execution_verified=false,labview_executed=false,labview_called=true,runtime_tested=false,
                        import_source=new{path=request.SourcePath,before_sha256=request.SourceHash,after_sha256=sourceAfter,bytes=request.SourceLength,unchanged=true,explicitly_opened_in_labview=false,saved_in_labview=false,executed=false},
                        source_file_unchanged=true,source_vis_saved=false,saved_direct_callees=direct,saved_callee_closure=after.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),
                        registered_ni_vi_lib=backend.NiRoot,static_vi_callee_audit_complete=true,static_dependency_extensions=new[]{".vi",".ctl"},dynamic_vi_and_dll_audit_performed=false,diagram_audit_performed=false,complete_replacement=false,
                        limitations=new[]{"Only explicit local .vi files and their uniquely named copies; no external VI dependency copying or relinking.","NI may automatically resolve dependencies while opening the copy; this importer never explicitly opens or saves the source or a rejected callee.","Only static VI/typedef callees are recursively restricted to registered NI vi.lib. Known vendor/legacy paths are refused; the directory policy does not authenticate every installed component's publisher. DLL calls, dynamic VI calls, hardware and runtime side effects are not certified.","No VI is run. ExecState=1 is static evidence, not runtime correctness or proof of all external dependencies.","Loaded Path is checked; disk hashes do not independently prove absence of unsaved NI in-memory edits."}};
                }
                // Publish success only after native references and the session
                // lease have been released cleanly.
                Storage.NoReparse(request.Target);Storage.NoReparse(request.SourcePath);
                if(Data.Hash(request.Target)!=targetHash)throw new IOException("Imported copy changed while closing the import session.");
                if(StreamHash(source)!=request.SourceHash||Data.Hash(request.SourcePath)!=request.SourceHash)throw new IOException("Source changed while closing the import session.");
                Storage.WriteNew(reportPath,report);return report;
            }catch(Exception ex) {
                string after=null;
                try{after=StreamHash(source);}catch(IOException){}
                try {Storage.WriteNew(Path.Combine(request.Folder,"import-failure.json"),new{created=false,error=Data.Error(ex),partial_vi_path=request.Target,partial_vi_exists=File.Exists(request.Target),source_path=request.SourcePath,source_before_sha256=request.SourceHash,source_after_sha256=after,source_file_unchanged=after==request.SourceHash,generated_vi_executed=false,source_saved=false});}
                catch(IOException){}catch(UnauthorizedAccessException){}
                throw;
            }
        }
    }
}
}
