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
// Explicit selected-file preparation. The worker reads native identities and
// static closure; no source VI, class business method, or copied VI is Run.
public static class TypeImport {
    public sealed class Prepared {
        public string Path,Hash,Kind,ReportPath,ReportHash;
        public int VIType;
        internal bool RetainedNiSource;
        internal string ClassSnapshotRoot;
        public Dictionary<string,object> Report;
        public void Verify() {
            Storage.NoReparse(Path);
            if(RetainedNiSource){if(!IsNativeFile(Path)||Data.Hash(Path)!=Hash)throw new IOException("Retained NI file changed or left the registered distribution.");return;}
            Storage.NoReparse(ReportPath);
            if(Data.Hash(Path)!=Hash||Data.Hash(ReportPath)!=ReportHash)throw new IOException("Prepared file/report changed.");
            if(ClassSnapshotRoot!=null){var bundle=ClassSnapshot.Accepted(Path);if(!String.Equals(bundle.Target,ClassSnapshotRoot,StringComparison.OrdinalIgnoreCase))throw new IOException("Prepared class member owner changed.");}
        }
    }
    sealed class Request {
        public string Source,Hash,Name,Folder,Target,RequestPath;
    }
    internal static bool IsPrepared(string path) {return File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(PhysicalCarrier(path)),"prepared-file-report.json"));}
    internal static bool IsPreparedDependency(string path) {return IsPrepared(path)||ClassSnapshot.HasReport(path);}
    internal static bool IsNativeDependency(string path) {return NiDistribution.AcceptDependency(PhysicalCarrier(path));}
    internal static bool IsNativeFile(string path) {return new[]{".vi",".ctl"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase)&&File.Exists(path)&&NiDistribution.AcceptDependency(path);}
    internal static void RejectVendorPath(string path) {if(path.Split(System.IO.Path.DirectorySeparatorChar).Any(part=>Regex.IsMatch(part,@"\A(VIRobotics|VIGenerator|VI Generator|VI_Generator(32|64)?|AgentForLabVIEW|Agent_For_LabVIEW(32|64)?)\z",RegexOptions.IgnoreCase)))throw new UnauthorizedAccessException("Known vendor runtime/source directory excluded: "+path);}
    internal static string PhysicalCarrier(string value) {
        string current=value.Replace('/',System.IO.Path.DirectorySeparatorChar);
        while(current.Length>3) {if(File.Exists(current)){string carrier=ViImport.CanonicalLocalPath(current);Storage.NoReparse(carrier);return carrier;}int cut=current.LastIndexOf(System.IO.Path.DirectorySeparatorChar);if(cut<3)break;current=current.Substring(0,cut);}
        throw new FileNotFoundException("Native dependency lacks an existing physical carrier.",value);
    }
    internal static string NativePath(string value) {
        string raw=value.Replace('/',System.IO.Path.DirectorySeparatorChar),carrier=PhysicalCarrier(raw);if(!raw.StartsWith(carrier,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Native physical carrier spelling is not canonical: "+value);string suffix=raw.Substring(carrier.Length);
        if(suffix.Length!=0&&suffix.Split(System.IO.Path.DirectorySeparatorChar).Skip(1).Any(s=>String.IsNullOrWhiteSpace(s)||s=="."||s==".."||s.EndsWith(".",StringComparison.Ordinal)||s.EndsWith(" ",StringComparison.Ordinal)||s.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0))throw new InvalidDataException("Native virtual member has an ambiguous suffix: "+value);
        return carrier+suffix;
    }
    static string InputPath(string value) {
        string path=ViImport.CanonicalLocalPath(value);string extension=System.IO.Path.GetExtension(path);
        if(!new[]{".vi",".ctl",".lvclass"}.Contains(extension,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Selected files must be .vi, .ctl or .lvclass.");
        if(!File.Exists(path)||new FileInfo(path).Length<1||new FileInfo(path).Length>268435456)throw new IOException("Selected file is missing, empty or exceeds256 MiB.");
        RejectVendorPath(path);
        return path;
    }
    static Request Parse(Dictionary<string,object> args) {
        Data.Keys(args,"source_path","name");string name=Data.Text(args,"name");
        if(!Regex.IsMatch(name,@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z")||Regex.IsMatch(name,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z",RegexOptions.IgnoreCase))throw new ArgumentException("Preparation name must be a non-reserved ASCII identifier of at most40 characters.");
        string source=InputPath(Data.Text(args,"source_path"));
        if(System.IO.Path.GetExtension(source).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)&&!NiDistribution.AcceptDependency(source))ClassSnapshot.Plan(source);
        return new Request{Name=name,Source=source,Hash=Data.Hash(source)};
    }
    public static object Validate(Dictionary<string,object> args) {
        var r=Parse(args);return new{valid=true,source_path=r.Source,source_sha256=r.Hash,labview_called=false,actual_kind_verified=false,scope="Selected file preflight; native VIType and NI/app static dependencies are checked by preparation. Class parent metadata is unexposed: literal class placement is checked without claiming full inheritance certification."};
    }
    internal static string ValidateClassPath(string value) {string path=InputPath(value);if(!System.IO.Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Actual .lvclass path required.");if(!NiDistribution.AcceptDependency(path))ClassSnapshot.Accepted(path);return path;}
    internal static Prepared ValidateSnapshotMember(string value) {
        string path=InputPath(value);var bundle=ClassSnapshot.Accepted(path);string reportPath=ClassSnapshot.ReportFor(path),reportHash=Data.Hash(reportPath);
        var report=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(reportPath,Encoding.UTF8)));
        var metadata=Data.Map(report["class_type_dependency"]);
        var members=Data.Array(metadata["members"]).Select(Data.Map).Where(row=>String.Equals(NativePath(Data.Text(row,"actual_vi_path")),path,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(members.Length!=1||Data.Text(members[0],"type_string")!="VI"||!String.Equals(NativePath(Data.Text(members[0],"path")),path,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Selected class snapshot file is not one exact accepted native VI member.");
        int type=Convert.ToInt32(members[0]["vi_type"]);string kind=Kind(type);
        if(kind=="control"!=System.IO.Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Class snapshot member extension and native VIType disagree.");
        string hash=Data.Hash(path);if(!String.Equals(PhysicalCarrier(Data.Text(members[0],"carrier")),path,StringComparison.OrdinalIgnoreCase)||Data.Text(members[0],"sha256")!=hash)throw new IOException("Class snapshot member native carrier/hash differs.");
        var prepared=new Prepared{Path=path,Hash=hash,Kind=kind,VIType=type,ReportPath=reportPath,ReportHash=reportHash,Report=report,ClassSnapshotRoot=bundle.Target};prepared.Verify();return prepared;
    }
    public static Prepared ValidatePrepared(string value) {
        string path=InputPath(value);string projects=System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,"projects"))+System.IO.Path.DirectorySeparatorChar;
        if(!path.StartsWith(projects,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Prepared copied files must be under own projects.");
        string reportPath=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"prepared-file-report.json");Storage.NoReparse(reportPath);
        if(!File.Exists(reportPath)||new FileInfo(reportPath).Length>1048576)throw new IOException("Prepared file report is unavailable or too large.");
        string reportHash=Data.Hash(reportPath);var report=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(reportPath,Encoding.UTF8)));
        if(!Object.Equals(report["created"],true)||!Object.Equals(report["native_identity_and_static_closure_checked"],true)||!String.Equals(InputPath(Data.Text(report,"vi_path")),path,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Prepared report does not identify an accepted file.");
        string kind=Data.Text(report,"category");int type=Convert.ToInt32(report["vi_type"]);
        if(type!=(kind=="subvi"?1:kind=="control"?2:kind=="global"?3:kind=="polymorphic"?4:-1))throw new InvalidDataException("Prepared kind and native VIType disagree.");
        if(kind=="control"&&!System.IO.Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase)||kind!="control"&&!System.IO.Path.GetExtension(path).Equals(".vi",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Prepared type and extension disagree.");
        var prepared=new Prepared{Path=path,Hash=Data.Text(report,"vi_sha256"),Kind=kind,VIType=type,ReportPath=reportPath,ReportHash=reportHash,Report=report};prepared.Verify();return prepared;
    }
    public static object Prepare(Dictionary<string,object> args) {
        var r=Parse(args);string token=Guid.NewGuid().ToString("N");r.Folder=System.IO.Path.Combine(Storage.Root,"projects",r.Name+"_Prepared_"+token);
        Storage.NoReparse(r.Folder);if(Directory.Exists(r.Folder))throw new IOException("Preparation output exists.");Directory.CreateDirectory(r.Folder);
        r.Target=System.IO.Path.Combine(r.Folder,System.IO.Path.GetFileName(r.Folder)+System.IO.Path.GetExtension(r.Source));r.RequestPath=System.IO.Path.Combine(r.Folder,"prepare-file-request.json");
        Storage.WriteNew(r.RequestPath,new{source_path=r.Source,source_sha256=r.Hash,output_path=r.Target,name=r.Name});
        // Parent owns no NI lock; workers acquire it once. Recipe uses Native
        // session below directly instead of calling this tool inside its lease.
        NiApplicationLifetime.EnsureForBuild();
        var start=new ProcessStartInfo(System.IO.Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--prepare-file-worker \""+r.RequestPath+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Storage.Root,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var child=new Process{StartInfo=start}) {
            child.Start();var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(180000)){child.Kill();child.WaitForExit(5000);throw new TimeoutException("Prepared-file worker timeout. Failure/recovery evidence retained: "+r.Folder);}
            if(child.ExitCode!=0)throw new InvalidOperationException("Prepared-file worker failed: "+error.Result+" Evidence: "+r.Folder);
            if(!output.Wait(5000))throw new IOException("Prepared-file output did not close.");
        }
        if(Data.Hash(r.Source)!=r.Hash)throw new IOException("Original selected source changed.");
        return Data.Json.DeserializeObject(File.ReadAllText(System.IO.Path.Combine(r.Folder,"prepared-file-report.json"),Encoding.UTF8));
    }
    internal static string Kind(int type){if(type==1)return "subvi";if(type==2)return "control";if(type==3)return "global";if(type==4)return "polymorphic";throw new ArgumentException("Unsupported actual source VIType: "+type);}
    internal static void ValidateExternalClassData(IEnumerable<object> members) {
        if(!members.Select(value=>Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(value)))).Any(row=>Data.Text(row,"type_string")=="Class Private Data"&&Convert.ToInt32(row["vi_type"])==2))throw new InvalidOperationException("External class has no actual native Class Private Data control; library metadata alone does not establish a placeable class type.");
    }
    public static object Worker(string requestPath) {
        requestPath=ViImport.CanonicalLocalPath(requestPath);string projects=System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,"projects"))+System.IO.Path.DirectorySeparatorChar;
        if(!requestPath.StartsWith(projects,StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetFileName(requestPath)!="prepare-file-request.json"||new FileInfo(requestPath).Length>1048576)throw new UnauthorizedAccessException("Preparation worker requires own isolated request.");
        var doc=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(requestPath,Encoding.UTF8)));var r=Parse(new Dictionary<string,object>{{"source_path",doc["source_path"]},{"name",doc["name"]}});
        r.Folder=System.IO.Path.GetDirectoryName(requestPath);r.Target=ViImport.CanonicalLocalPath(Data.Text(doc,"output_path"));
        if(r.Hash!=Data.Text(doc,"source_sha256")||!String.Equals(System.IO.Path.GetDirectoryName(r.Target),r.Folder,StringComparison.OrdinalIgnoreCase)||File.Exists(r.Target)||File.Exists(System.IO.Path.Combine(r.Folder,"prepared-file-report.json")))throw new IOException("Preparation identity changed or output exists.");
        var refs=new List<object>();object app=null;
        using(var lease=new NiSessionLease())try {
            app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();using(var native=new Session(app,refs)) {
                if(System.IO.Path.GetExtension(r.Source).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)) {
                    if(NiDistribution.AcceptDependency(r.Source)) {
                        // NI classes keep the distribution's physical carriers.
                        var classMetadata=native.ClassMetadata(r.Source);var classClosure=native.Audit((string[])classMetadata["native_callees"]);
                        if(Data.Hash(r.Source)!=r.Hash)throw new IOException("Selected class source changed.");
                        var classReport=new{created=true,category="class",vi_path=r.Source,vi_sha256=r.Hash,native_identity_and_static_closure_checked=true,class_type_dependency=classMetadata,native_dependency_closure=classClosure,source_path=r.Source,source_sha256=r.Hash,source_files_unchanged=true,source_saved=false,source_executed=false,generated_vi_executed=false,prepared_copy_created=false,helper_paths=native.HelperPaths,parent_metadata_unexposed=false,inheritance_closure_certified=true};
                        Storage.WriteNew(System.IO.Path.Combine(r.Folder,"prepared-file-report.json"),classReport);return classReport;
                    }
                    // Do not open the original external class. Copy its entire
                    // physical bundle first; native reads must bind to that copy.
                    var snapshot=ClassSnapshot.Copy(r.Source,r.Folder);
                    // A class name identifies one library per application
                    // instance. Read the relocated bundle in our own empty
                    // project instance so NI cannot return a same-named class
                    // already loaded by the author's scratch project.
                    object snapshotProject=LabView.Invoke(app,"NewProject",BindingFlags.InvokeMethod);
                    refs.Add(snapshotProject);
                    object snapshotApp=LabView.Invoke(snapshotProject,"Application",BindingFlags.GetProperty);
                    refs.Add(snapshotApp);
                    try {using(var copied=new Session(snapshotApp,refs,snapshot)) {
                        var classMetadata=copied.ClassMetadata(snapshot.Target);var classClosure=copied.Audit((string[])classMetadata["native_callees"]);snapshot.VerifySources();
                        var classReport=new{created=true,category="class",vi_path=snapshot.Target,vi_sha256=Data.Hash(snapshot.Target),native_identity_and_static_closure_checked=true,class_type_dependency=classMetadata,native_dependency_closure=classClosure,source_path=r.Source,source_sha256=r.Hash,source_bundle_root=snapshot.SourceRoot,snapshot_files=snapshot.Rows(),snapshot_xml_relinks=snapshot.Relinks.ToArray(),source_files_unchanged=true,source_saved=false,source_executed=false,generated_vi_executed=false,prepared_copy_created=true,helper_paths=copied.HelperPaths,parent_metadata_unexposed=false,inheritance_closure_certified=true};
                        Storage.WriteNew(System.IO.Path.Combine(r.Folder,"prepared-file-report.json"),classReport);return classReport;
                    }} finally {LabView.Invoke(snapshotProject,"Close",BindingFlags.InvokeMethod);}
                }
                object original=native.Open(r.Source);int type=native.Type(original);string kind=Kind(type);
                if(kind=="control"&&!System.IO.Path.GetExtension(r.Source).Equals(".ctl",StringComparison.OrdinalIgnoreCase)||kind!="control"&&!System.IO.Path.GetExtension(r.Source).Equals(".vi",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Selected file extension and actual VIType disagree.");
                if((kind=="subvi"||kind=="polymorphic")&&Convert.ToInt32(LabView.Invoke(original,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Executable/poly source must be compiled idle.");
                object[] sourceClosure=native.Audit(Session.Paths(original));var controls=kind=="control"||kind=="global"?native.Controls(r.Source):new object[0];
                if(kind=="polymorphic"&&IsNativeFile(r.Source)) {
                    // An NI poly can use its own basename/carrier for instance
                    // metadata. Preserve the reviewed original NI path, rather
                    // than relabeling exact bytes into a broken polymorphic VI.
                    int sourceState=Convert.ToInt32(LabView.Invoke(original,"ExecState",BindingFlags.GetProperty));if(sourceState!=1)throw new InvalidOperationException("Selected NI polymorphic source must be compiled idle.");
                    if(Data.Hash(r.Source)!=r.Hash)throw new IOException("Selected NI poly source changed.");
                    var retainedReport=new{created=true,category=kind,vi_path=r.Source,vi_sha256=r.Hash,vi_type=type,execution_state=sourceState,native_identity_and_static_closure_checked=true,controls,saved_direct_callees=Session.Paths(original),native_dependency_closure=sourceClosure,source_dependency_closure=sourceClosure,source_path=r.Source,source_sha256=r.Hash,source_files_unchanged=true,source_saved=false,source_executed=false,generated_vi_executed=false,prepared_copy_created=false,retained_ni_source=true,helper_paths=native.HelperPaths};
                    Storage.WriteNew(System.IO.Path.Combine(r.Folder,"prepared-file-report.json"),retainedReport);return retainedReport;
                }
                using(var source=new FileStream(r.Source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var target=new FileStream(r.Target,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(target);
                if(Data.Hash(r.Source)!=r.Hash||Data.Hash(r.Target)!=r.Hash)throw new IOException("Selected file copy did not preserve exact source bytes.");
                object copy=native.Open(r.Target);if(native.Type(copy)!=type)throw new InvalidOperationException("Copied native VIType changed.");
                if((kind=="subvi"||kind=="polymorphic")&&Convert.ToInt32(LabView.Invoke(copy,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Copied executable/poly is not compiled idle; exact-byte relocation did not preserve a valid dependency/instance carrier. Original source was not Save/Run.");
                object[] copiedClosure=native.Audit(Session.Paths(copy));
                var copiedControls=kind=="control"||kind=="global"?native.Controls(r.Target):new object[0];
                if(Data.Json.Serialize(controls)!=Data.Json.Serialize(copiedControls))throw new InvalidOperationException("Copied control labels/classes changed.");
                var report=new{created=true,category=kind,vi_path=r.Target,vi_sha256=Data.Hash(r.Target),vi_type=type,execution_state=Convert.ToInt32(LabView.Invoke(copy,"ExecState",BindingFlags.GetProperty)),native_identity_and_static_closure_checked=true,controls=copiedControls,saved_direct_callees=Session.Paths(copy),native_dependency_closure=copiedClosure,source_dependency_closure=sourceClosure,source_path=r.Source,source_sha256=r.Hash,source_files_unchanged=Data.Hash(r.Source)==r.Hash,source_saved=false,source_executed=false,generated_vi_executed=false,prepared_copy_created=true,helper_paths=native.HelperPaths};
                if(!report.source_files_unchanged)throw new IOException("Selected source changed during native read.");
                Storage.WriteNew(System.IO.Path.Combine(r.Folder,"prepared-file-report.json"),report);
                if(kind=="control"||kind=="global")Storage.WriteNew(System.IO.Path.Combine(r.Folder,"type-report.json"),new{created=true,artifact_kind=kind,vi_path=r.Target,vi_sha256=Data.Hash(r.Target),vi_type=type,static_checks_passed=true,controls=copiedControls,saved_direct_callees=Session.Paths(copy),native_dependency_closure=copiedClosure,generated_vi_executed=false,imported_source=r.Source,source_sha256=r.Hash,source_files_unchanged=true});
                return report;
            }
        }catch(Exception ex){string failure=System.IO.Path.Combine(r.Folder,"prepare-file-failure.json");if(!File.Exists(failure))Storage.WriteNew(failure,new{created=false,error=Data.Error(ex),source_path=r.Source,source_sha256_before=r.Hash,source_sha256_after=File.Exists(r.Source)?Data.Hash(r.Source):null,source_saved=false,source_executed=false,target_executed=false});throw;}
        finally {for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);LabView.Release(app);}
    }
    // Borrowed session only: no lease, no app release, references held by owner.
    internal sealed class Session:IDisposable {
        readonly object app;readonly List<object> refs;readonly ClassSnapshot.Bundle pendingSnapshot;readonly Dictionary<string,object> helpers=new Dictionary<string,object>();
        internal readonly Dictionary<string,string> HelperPaths=new Dictionary<string,string>();
        internal Session(object application,List<object> references){app=application;refs=references;}
        internal Session(object application,List<object> references,ClassSnapshot.Bundle snapshot):this(application,references){pendingSnapshot=snapshot;}
        bool SnapshotDependency(string path) {if(pendingSnapshot!=null&&pendingSnapshot.Contains(path))return pendingSnapshot.VerifyDependency(path);if(ClassSnapshot.HasReport(path)){ClassSnapshot.Accepted(path);return true;}return false;}
        internal object Open(string path) {path=NativePath(path);object vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(vi);if(!String.Equals(NativePath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))),path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Selected module resolved to a different actual Path: "+path);return vi;}
        internal int Type(object vi){return Convert.ToInt32(LabView.Invoke(vi,"VIType",BindingFlags.GetProperty));}
        internal static string[] Paths(object vi){return ((IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)).Cast<object>().Select(Convert.ToString).Where(p=>!String.IsNullOrWhiteSpace(p)).ToArray();}
        static string Carrier(string path) {return PhysicalCarrier(path);}
        internal object[] Audit(IEnumerable<string> paths) {
            var queue=new Queue<string>(paths);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var rows=new List<object>();
            while(queue.Count!=0) {
                string path=NativePath(queue.Dequeue());RejectVendorPath(path);if(!seen.Add(path))continue;if(seen.Count>256)throw new InvalidDataException("Static dependency budget exceeded.");
                string carrier=Carrier(path);bool ni=NiDistribution.AcceptDependency(carrier);bool own=path.StartsWith(System.IO.Path.Combine(Storage.Root,"projects")+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);bool snapshot=!ni&&own&&SnapshotDependency(path);
                // An NI class-private virtual suffix can exceed Windows' file
                // limit. Its physical carrier is already canonical and pinned;
                // do not feed that suffix to a physical open-source file guard.
                bool approved=!ni&&!own&&ApprovedOpenSource.Accept(path);
                if(!ni&&!own&&!approved)throw new UnauthorizedAccessException("Selected source static dependency is outside NI/own prepared/pinned open source assets: "+path);
                Storage.NoReparse(carrier);string hash=Data.Hash(carrier);
                if(System.IO.Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)) {
                    var metadata=ClassMetadata(path);foreach(string callee in (string[])metadata["native_callees"])queue.Enqueue(callee);rows.Add(new{path,carrier,sha256=hash,edge="native_typed_library",type_metadata=metadata});
                } else {
                    if(System.IO.Path.GetExtension(path).Equals(".lvlib",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Native typed-library metadata is unsupported for: "+path);
                    if(own&&!snapshot) {if(IsPrepared(path))ValidatePrepared(path).Verify();else if(System.IO.Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase))GeneratedTypeDependency.ValidateOne(path).VerifyUnchanged();else GeneratedViDependency.ValidateOne(path).VerifyUnchanged();}
                    object vi=Open(path);string[] callees=Paths(vi);foreach(string callee in callees)queue.Enqueue(callee);
                    rows.Add(new{path,carrier,sha256=hash,edge="native_vi_callee",vi_type=Type(vi),actual_path=path,native_callees=callees});
                }
                if(Data.Hash(carrier)!=hash)throw new IOException("Static dependency carrier changed during read.");
            }
            return rows.ToArray();
        }
        internal Dictionary<string,object> ClassMetadata(string source) {
            source=InputPath(source);if(!NiDistribution.AcceptDependency(source)&&!SnapshotDependency(source))throw new UnauthorizedAccessException("Native class requires NI or its pinned own snapshot: "+source);string hash=Data.Hash(source);object library=LabView.Invoke(app,"OpenLibrary",BindingFlags.InvokeMethod,source);refs.Add(library);
            string actualLibrary=Convert.ToString(LabView.Invoke(library,"Path",BindingFlags.GetProperty)),actualType=Convert.ToString(LabView.Invoke(library,"TypeString",BindingFlags.GetProperty));if(!String.Equals(System.IO.Path.GetFullPath(actualLibrary),source,StringComparison.OrdinalIgnoreCase)||actualType!="LVClass")throw new InvalidOperationException("Selected native library Path/type differs: requested="+source+", actual="+actualLibrary+", type="+actualType);
            var members=new List<object>();var callees=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VisitClassItems(library,source,0,members,callees,identities);
            var parentHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var nativeInterfaces=ClassInterfaces.Info(app,refs,source,new HashSet<string>(StringComparer.OrdinalIgnoreCase),parentHashes);
            var nativeParent=(bool)nativeInterfaces["is_interface"]?new Dictionary<string,object>{{"path",""},{"name",""},{"no_parent",true},{"interface_has_no_class_parent",true}}:ClassOop.ParentInfo(app,refs,source,new HashSet<string>(StringComparer.OrdinalIgnoreCase),parentHashes);
            string parentPath=Convert.ToString(nativeParent["path"]);
            if(parentPath.Length!=0){parentPath=InputPath(parentPath);if(!NiDistribution.AcceptDependency(parentPath)&&!SnapshotDependency(parentPath))throw new UnauthorizedAccessException("Native parent is outside the pinned class bundle/NI: "+parentPath);if(parentPath.Equals(source,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Native class cannot be its own parent.");callees.Add(parentPath);}
            // A library whose only XML member is a method can open as LVClass,
            // but has no native class data type. The real NewVIObject call on
            // that fixture returned1061; reject before a placement helper Run.
            foreach(string path in (string[])nativeInterfaces["parent_interfaces"]){if(!NiDistribution.AcceptDependency(path)&&!SnapshotDependency(path))throw new UnauthorizedAccessException("Native interface is outside the pinned class bundle/NI: "+path);callees.Add(path);}
            if(!NiDistribution.AcceptDependency(source)&&!(bool)nativeInterfaces["is_interface"])ValidateExternalClassData(members);
            if(Data.Hash(source)!=hash)throw new IOException("Class source carrier changed.");
            return new Dictionary<string,object>{{"path",source},{"sha256",hash},{"native_library_type","LVClass"},{"members",members.ToArray()},{"native_callees",callees.ToArray()},{"native_parent",nativeParent},{"native_interfaces",nativeInterfaces},{"parent_reader_hashes",parentHashes},{"parent_metadata_unexposed",false},{"native_parent_identity_checked",true},{"inheritance_closure_certified",false},{"scope","Actual loaded library, private data or native interface identity, members and native parents. Audit traverses parents and static method dependencies; runtime dispatch has separate finite fixtures."}};
        }
        void VisitClassItems(object parent,string source,int depth,List<object> members,HashSet<string> callees,HashSet<string> identities) {
            if(depth>16)throw new InvalidDataException("Class folder depth exceeds16.");
            foreach(object item in (IEnumerable)LabView.Invoke(parent,"Children",BindingFlags.GetProperty)) {
                refs.Add(item);string kind=Convert.ToString(LabView.Invoke(item,"TypeString",BindingFlags.GetProperty));string name=Convert.ToString(LabView.Invoke(item,"Name",BindingFlags.GetProperty));
                if(kind.IndexOf("Folder",StringComparison.OrdinalIgnoreCase)>=0){VisitClassItems(item,source,depth+1,members,callees,identities);continue;}
                string path=NativePath(Convert.ToString(LabView.Invoke(item,"Path",BindingFlags.GetProperty)));RejectVendorPath(path);
                if(!identities.Add(path)||identities.Count>256||(!NiDistribution.AcceptDependency(Carrier(path))&&!SnapshotDependency(path)))throw new InvalidOperationException("Class member duplicate, budget or external/vendor dependency: "+path);Storage.NoReparse(Carrier(path));
                object vi;
                if(kind=="Class Private Data") {vi=LabView.Invoke(item,"VI",BindingFlags.GetProperty);refs.Add(vi);if(!path.StartsWith(source+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Class private data carrier differs.");}
                else if(kind=="VI")vi=Open(path);
                else throw new InvalidOperationException("Unsupported native class member kind "+kind+" at "+path);
                string actual=NativePath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty)));if(!String.Equals(actual,path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Native class member resolves to a different Path.");
                int type=Type(vi);if(kind=="Class Private Data"?type!=2:!(type>=1&&type<=4)||System.IO.Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase)!=(type==2))throw new InvalidOperationException("Class member native VIType/extension differs: "+path);
                if(type==1&&Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidDataException("Native class member is not executable: "+path);
                string carrier=Carrier(path),hash=Data.Hash(carrier);string[] children=Paths(vi);foreach(string callee in children)callees.Add(callee);
                members.Add(new{path,name,type_string=kind,item_id=Convert.ToString(LabView.Invoke(item,"ItemID",BindingFlags.GetProperty)),actual_vi_path=actual,vi_type=type,native_callees=children,carrier,sha256=hash});
                if(Data.Hash(carrier)!=hash)throw new IOException("Class member carrier changed.");
            }
        }
        internal Dictionary<string,object> Call(string name,Dictionary<string,object> inputs,params string[] outputs) {
            object helper;if(!helpers.TryGetValue(name,out helper)) {
                string path=null,hash=null;
                foreach(string manifestName in new[]{"file-helpers/manifest.json","type-helpers/provenance.json","recipes/helper-provenance.json"}) {
                    string manifestPath=System.IO.Path.Combine(Storage.Root,manifestName.Replace('/',System.IO.Path.DirectorySeparatorChar));Storage.NoReparse(manifestPath);
                    var entries=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(manifestPath,Encoding.UTF8)))["helpers"]);object value;
                    if(!entries.TryGetValue(name,out value))continue;var entry=Data.Map(value);
                    path=entry.ContainsKey("relative_path")?System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,Data.Text(entry,"relative_path"))):System.IO.Path.Combine(Storage.Root,"recipes","helpers",name+".vi");hash=Data.Text(entry,"sha256");break;
                }
                if(path==null||!path.StartsWith(Storage.Root+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Own pinned file helper unavailable: "+name);Storage.NoReparse(path);
                if(Data.Hash(path)!=hash)throw new IOException("Own file helper changed: "+name);helper=Open(path);
                if(Convert.ToInt32(LabView.Invoke(helper,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Own file helper is not compiled idle: "+name);
                Audit(Paths(helper));helpers[name]=helper;HelperPaths["prepared:"+name]=path;
            }
            foreach(var pair in inputs){if(pair.Key.EndsWith("_id",StringComparison.Ordinal)&&Convert.ToUInt32(pair.Value)==0)throw new ArgumentException("Null file reference: "+pair.Key);LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,pair.Key,pair.Value);}
            LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);var error=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"error out") as IList;
            if(error==null||error.Count!=3||Convert.ToBoolean(error[0]))throw new InvalidOperationException(name+" NI helper error: "+(error==null?"missing error cluster":Convert.ToString(error[1])+": "+error[2]));
            var result=new Dictionary<string,object>();foreach(string output in outputs)result[output]=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,output);return result;
        }
        internal object[] Controls(string path) {
            var ids=(IEnumerable)Call("Native_GetPanelControls",new Dictionary<string,object>{{"target_path",path}},"control_ids")["control_ids"];var rows=new List<object>();
            foreach(object raw in ids) {uint id=Convert.ToUInt32(raw);if(id==0||rows.Count>=256)throw new InvalidOperationException("Null control or selected panel exceeds256 controls.");
                var label=Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",id}},"label");var info=Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name");
                rows.Add(new{label=Convert.ToString(label["label"]),actual_class=Convert.ToString(info["class_name"])});
            }
            return rows.ToArray();
        }
        internal void RecordHelpers(Dictionary<string,string> loadedPaths){foreach(var pair in HelperPaths)loadedPaths[pair.Key]=pair.Value;}
        public void Dispose() {helpers.Clear();}
    }
}
}
