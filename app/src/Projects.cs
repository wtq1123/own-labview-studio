using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace OwnLabView {
// Creates a project referencing VIs already produced by this application's builders.
// Does not run a VI, edit its diagram, or save any source VI.
public static class Projects {
    sealed class Member {
        public string Path, Hash, Provenance;
        public Dictionary<string,string> Related = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    }
    sealed class Request {
        public string Name, ProjectsRoot;
        public ProjectSource Source;
        public Dictionary<string,object>[] Specs;public bool Build;public ProjectLayout Layout;
        public List<Member> Members = new List<Member>();
    }
    static readonly Regex NamePattern = new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    static readonly Regex ReservedName = new Regex(@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase);

    static string MemberPath(string value, string projectsRoot) {
        if (String.IsNullOrWhiteSpace(value) || !System.IO.Path.IsPathRooted(value))
            throw new ArgumentException("Project members must use absolute VI paths.");
        string full = System.IO.Path.GetFullPath(value);
        string prefix = projectsRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Project members must be inside this application's projects folder.");
        if (!new[]{".vi",".ctl",".lvclass",".lvlib"}.Contains(System.IO.Path.GetExtension(full),StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Project members must be .vi, .ctl or .lvclass files.");
        if (full.Length > 2 && full.Substring(2).IndexOf(':') >= 0)
            throw new UnauthorizedAccessException("Alternate data stream paths are not accepted.");
        Storage.NoReparse(full);
        if (!File.Exists(full)) throw new FileNotFoundException("Generated VI does not exist.", full);
        return full;
    }

    static string Provenance(string path, string hash, string projectsRoot) {
        if(Path.GetExtension(path).Equals(".lvlib",StringComparison.OrdinalIgnoreCase)){Libraries.Validate(path);return Path.Combine(Path.GetDirectoryName(path),"library-report.json");}
        if(System.IO.Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)&&ClassSnapshot.HasReport(path)){var bundle=ClassSnapshot.Accepted(path);return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bundle.Root),"prepared-file-report.json");}
        if(TypeImport.IsPreparedDependency(path)){var prepared=ClassSnapshot.HasReport(path)?TypeImport.ValidateSnapshotMember(path):TypeImport.ValidatePrepared(path);prepared.Verify();return prepared.ReportPath;}
        if(System.IO.Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)){ClassOop.ParentReport(path);return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)),"type-report.json");}
        if(File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"type-report.json"))){var type=GeneratedTypeDependency.ValidateOne(path);return type.ReportPath;}

        foreach (string name in new[] { "build-report.json", "recipe-report.json" }) {
            string candidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), name);
            if (!File.Exists(candidate)) continue;
            Storage.NoReparse(candidate);
            if (new FileInfo(candidate).Length > 1048576)
                throw new InvalidDataException("Generation report exceeds the supported size: " + candidate);
            var report = Data.Map(Data.Json.DeserializeObject(File.ReadAllText(candidate, Encoding.UTF8)));
            object created;
            if (!report.TryGetValue("created", out created) || !Object.Equals(created, true)) continue;
            string recorded = MemberPath(Data.Text(report, "vi_path"), projectsRoot);
            if (!String.Equals(recorded, path, StringComparison.OrdinalIgnoreCase)) continue;
            if (!String.Equals(Data.Text(report, "vi_sha256"), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated VI no longer matches its build report: " + path);
            return candidate;
        }
        throw new ArgumentException("VI requires a matching successful build-report.json or recipe-report.json: " + path);
    }

    internal static string ValidateMember(string path){string root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,"projects"));path=MemberPath(path,root);string report=Provenance(path,Data.Hash(path),root);if(new[]{"build-report.json","recipe-report.json"}.Contains(System.IO.Path.GetFileName(report)))GeneratedViDependency.ValidateOne(path);return report;}
    static Request Parse(Dictionary<string, object> args) {
        if (args == null) throw new ArgumentNullException("args");
        Data.Keys(args, "name", "vi_paths", "source_project", "build_specs", "build", "folders", "member_folders");
        string name = Data.Text(args, "name");
        if (!NamePattern.IsMatch(name) || ReservedName.IsMatch(name))
            throw new ArgumentException("Project name must be a non-reserved ASCII identifier of at most 40 characters.");
        object raw;
        if (!args.TryGetValue("vi_paths", out raw)) throw new ArgumentException("Missing vi_paths.");
        object[] paths = Data.Array(raw);
        if (paths.Length > 64) throw new ArgumentException("vi_paths must contain 0 to64 generated members.");
        var request = new Request { Name = name, ProjectsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root, "projects")) };
        if(args.ContainsKey("source_project"))request.Source=ProjectSource.Validate(Data.Text(args,"source_project"));
        Storage.NoReparse(request.ProjectsRoot);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object value in paths) {
            if (!(value is string)) throw new ArgumentException("Every vi_paths entry must be a string.");
            string path = MemberPath((string)value, request.ProjectsRoot);
            if (!seen.Add(path)) throw new ArgumentException("Duplicate project member path: " + path);
            if (!names.Add(System.IO.Path.GetFileName(path)))
                throw new ArgumentException("Different members have the same VI filename; resolve LabVIEW name ambiguity first.");
            string hash = Data.Hash(path);
            var member = new Member { Path = path, Hash = hash, Provenance = Provenance(path, hash, request.ProjectsRoot) };
            if(System.IO.Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))member.Related=ClassSnapshot.Plan(path).Sources;
            if(Path.GetExtension(path).Equals(".lvlib",StringComparison.OrdinalIgnoreCase))foreach(var row in Data.Array(Libraries.Validate(path)["files"]).Select(Data.Map))member.Related[Data.Text(row,"path")]=Data.Text(row,"sha256");
            request.Members.Add(member);
        }
        request.Specs=BuildSpecs.Parse(args,request.Members.Select(m=>m.Path).ToArray(),request.Source);request.Build=args.ContainsKey("build")&&(bool)args["build"];
        request.Layout=ProjectLayout.Parse(args,request.Members.Select(m=>m.Path).ToArray(),request.Source);
        return request;
    }

    // Filesystem-only preflight. It neither attaches to LabVIEW nor creates output.
    public static object Validate(Dictionary<string, object> args) {
        Request request = Parse(args);
        return new { valid = true, name = request.Name, member_count = request.Members.Count,
            source_project=request.Source==null?null:request.Source.Evidence(),
            members = request.Members.Select(m => new { path = m.Path, sha256 = m.Hash, generation_report = m.Provenance }).ToArray(),
            labview_called = false, vi_format_verified = false,
            scope = "Paths, provenance reports and disk hashes only; VI validity is not tested by preflight." };
    }

    static List<object> SourceChecks(Request request, out bool unchanged) {
        unchanged = true;
        var checks = new List<object>();
        foreach (Member member in request.Members) {
            string after = null, error = null;
            try { MemberPath(member.Path, request.ProjectsRoot); after = Data.Hash(member.Path); }
            catch (Exception ex) { error = Data.Error(ex); }
            bool same = error == null && String.Equals(after, member.Hash, StringComparison.OrdinalIgnoreCase) && member.Related.All(p=>File.Exists(p.Key)&&Data.Hash(p.Key)==p.Value);
            if (!same) unchanged = false;
            checks.Add(new { path = member.Path, before_sha256 = member.Hash, after_sha256 = after, unchanged = same, related_files = member.Related, error });
        }
        return checks;
    }

    static List<object> VerifyXml(string projectPath, Request request) {
        Storage.NoReparse(projectPath);
        if (!File.Exists(projectPath)) throw new IOException("NI did not save the project file.");
        var xml = new XmlDocument { XmlResolver = null };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 };
        using (var reader = XmlReader.Create(projectPath, settings)) xml.Load(reader);
        if (xml.DocumentElement == null || xml.DocumentElement.LocalName != "Project")
            throw new InvalidDataException("Saved file is not a LabVIEW project XML document.");
        var observed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Verify actual members under My Computer, excluding transitive dependency lists.
        foreach (XmlNode node in xml.SelectNodes("/*[local-name()='Project']/*[local-name()='Item' and @Type='My Computer']//*[local-name()='Item' and (@Type='VI' or @Type='LVClass' or @Type='Library') and not(ancestor::*[@Type='Dependencies'])]")) {
            string url = node.Attributes["URL"] == null ? null : node.Attributes["URL"].Value;
            if (String.IsNullOrWhiteSpace(url)) throw new InvalidDataException("Project VI member has no URL.");
            string decoded = Uri.UnescapeDataString(url);
            string candidate;
            Uri absolute;
            if (Uri.TryCreate(decoded, UriKind.Absolute, out absolute) && absolute.IsFile)
                candidate = absolute.LocalPath;
            else {
                if (decoded.IndexOf("://", StringComparison.Ordinal) >= 0)
                    throw new InvalidDataException("Non-file URL in saved VI member.");
                // NI project relative paths treat the project FILE as the base
                // path: ../Member.vi is beside Project.lvproj. Verified through
                // OpenProject -> MyComputer.Children[].Path on saved NI projects.
                candidate = System.IO.Path.Combine(projectPath, decoded.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
            string full = MemberPath(candidate, request.ProjectsRoot);
            if (observed.ContainsKey(full)) throw new InvalidDataException("Duplicate member URL in saved project: " + full);
            observed.Add(full, url);
        }
        var expected = new HashSet<string>(request.Members.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
        if (!expected.SetEquals(observed.Keys))
            throw new InvalidDataException("Saved project members differ from the requested generated VI paths.");
        return request.Members.Select(m => (object)new { path = m.Path, xml_url = observed[m.Path], matched = true }).ToList();
    }

    public static object Create(Dictionary<string, object> args) {
        Request request = Parse(args);
        NiApplicationLifetime.EnsureForBuild();
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(request.ProjectsRoot,
            request.Name + "_Project_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
        if (!folder.StartsWith(request.ProjectsRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Project output escaped the application projects root.");
        Storage.NoReparse(folder);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("New project output already exists.");
        Directory.CreateDirectory(folder);
        Storage.NoReparse(folder);
        string reportPath = System.IO.Path.Combine(folder, "project-report.json");
        string requestPath=System.IO.Path.Combine(folder,"project-request.json");
        Storage.WriteNew(requestPath, args);
        var start=new ProcessStartInfo(System.IO.Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--project-worker \""+requestPath+"\"") {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Storage.Root,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        using(var child=new Process{StartInfo=start}) {
            child.Start();
            var output=child.StandardOutput.ReadToEndAsync();
            var errors=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(900000)) {
                child.Kill();child.WaitForExit(5000);
                throw new TimeoutException("Project worker timed out. Partial files and the NI recovery record are retained: "+folder);
            }
            if(child.ExitCode!=0)throw new InvalidOperationException("Project creation failed: "+errors.Result+" Partial project: "+folder);
            if(!output.Wait(5000))throw new IOException("Project worker did not close its output pipe.");
        }
        if(!File.Exists(reportPath))throw new IOException("Project worker exited without a report.");
        return Data.Json.DeserializeObject(File.ReadAllText(reportPath,Encoding.UTF8));
    }

    public static object Worker(string requestPath) {
        requestPath=System.IO.Path.GetFullPath(requestPath);Storage.NoReparse(requestPath);
        if(!requestPath.StartsWith(System.IO.Path.Combine(Storage.Root,"projects")+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||System.IO.Path.GetFileName(requestPath)!="project-request.json")
            throw new UnauthorizedAccessException("Project worker accepts isolated project-request.json only.");
        if(new FileInfo(requestPath).Length>1048576)throw new InvalidDataException("Project request is too large.");
        Request request=Parse(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(requestPath,Encoding.UTF8))));
        string folder=System.IO.Path.GetDirectoryName(requestPath);
        string projectPath=System.IO.Path.Combine(folder,request.Name+".lvproj");
        string reportPath=System.IO.Path.Combine(folder,"project-report.json");
        if(File.Exists(projectPath)||File.Exists(reportPath))throw new IOException("Project output already exists; refusing overwrite.");
        object app = null, project = null, computer = null;
        var itemReferences = new List<object>();
        var addedNames = new List<string>();
        string phase="attach_labview";
        var lease=new NiSessionLease();
        try {
            // This explicit creation method may activate NI LabVIEW; preflight does not.
            app = Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application", true));
            lease.Attached();
            if(request.Source==null)project = LabView.Invoke(app, "NewProject", BindingFlags.InvokeMethod);
            else {
                phase="write_project_copy";
                request.Source.WriteCopy(projectPath,request.Members.Select(m=>m.Path));
                phase="open_project_copy";
                project=LabView.Invoke(app,"OpenProject",BindingFlags.InvokeMethod,projectPath);
            }
            computer = LabView.Invoke(project, "MyComputer", BindingFlags.GetProperty);
            foreach (Member member in request.Members) {
                MemberPath(member.Path, request.ProjectsRoot);
                if (Data.Hash(member.Path) != member.Hash) throw new IOException("VI changed after project preflight: " + member.Path);
                if(request.Source!=null){addedNames.Add(System.IO.Path.GetFileName(member.Path));continue;}
                object item = LabView.Invoke(computer, "AddFile", BindingFlags.InvokeMethod, member.Path);
                itemReferences.Add(item);
                if(!String.Equals(TypeImport.NativePath(Convert.ToString(LabView.Invoke(item,"Path",BindingFlags.GetProperty))),member.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Project member actual path differs.");
                addedNames.Add(Convert.ToString(LabView.Invoke(item, "Name", BindingFlags.GetProperty)));
            }
            Storage.NoReparse(projectPath);
            if (request.Source==null&&File.Exists(projectPath)) throw new IOException("Project file already exists; refusing overwrite.");
            phase="save_project";
            LabView.Invoke(project, "Save", BindingFlags.InvokeMethod, projectPath);
            phase="project_layout";
            for(int i=itemReferences.Count-1;i>=0;i--)LabView.Release(itemReferences[i]);itemReferences.Clear();
            LabView.Invoke(project,"Close",BindingFlags.InvokeMethod);LabView.Release(computer);LabView.Release(project);computer=null;project=null;
            request.Layout.Apply(projectPath);phase="reopen_layout";project=LabView.Invoke(app,"OpenProject",BindingFlags.InvokeMethod,projectPath);computer=LabView.Invoke(project,"MyComputer",BindingFlags.GetProperty);phase="save_layout";LabView.Invoke(project,"Save",BindingFlags.InvokeMethod,projectPath);
            phase="build_specifications";
            object[] buildResults=BuildSpecs.Apply(app,computer,itemReferences,projectPath,request.Specs,request.Build);
            phase="verify_saved_project";
            List<object> xmlReferences = VerifyXml(projectPath, request);
            bool unchanged;
            List<object> sourceChecks = SourceChecks(request, out unchanged);
            if (!unchanged) throw new IOException("A source VI changed or became inaccessible during project creation; inspect project-failure.json.");
            object report = new { created = true, project_path = projectPath, project_sha256 = Data.Hash(projectPath),
                layout=request.Layout.Evidence(),build_specs=request.Specs,build_results=buildResults,
                member_count = request.Members.Count, added_item_names = addedNames, xml_member_references = xmlReferences,
                source_checks = sourceChecks, source_vis_unchanged = true,
                source_project=request.Source==null?null:request.Source.Evidence(),
                project_operation=request.Source==null?"create":"edit_members_in_new_copy",
                labview_version = Convert.ToString(LabView.Invoke(app, "Version", BindingFlags.GetProperty)),
                generated_vis_executed = false, source_vis_saved = false, dependency_audit_performed = false,
                complete_replacement = false,
                limitations = new[] { "Creates a project referencing existing generated VIs; does not copy or rewrite them.",
                    "XML membership and source disk hashes verified; project reopen, compilation and runtime are separate acceptance steps.",
                    "Callee paths and transitive dependencies are not audited by project creation; no VI is run." } };
            Storage.WriteNew(reportPath, report);
            return report;
        } catch (Exception ex) {
            bool unchanged;
            List<object> sourceChecks = SourceChecks(request, out unchanged);
            string failure = System.IO.Path.Combine(folder, "project-failure.json");
            try {
                if (!File.Exists(failure)) Storage.WriteNew(failure, new { created = false, phase, error = Data.Error(ex),
                    partial_project_path = projectPath, project_file_exists = File.Exists(projectPath),
                    source_checks = sourceChecks, source_vis_unchanged = unchanged, generated_vis_executed = false });
            } catch (Exception reportError) {
                throw new IOException("Project creation failed and its failure report could not be written: " + Data.Error(reportError), ex);
            }
            throw;
        } finally {
            for (int i = itemReferences.Count - 1; i >= 0; i--) LabView.Release(itemReferences[i]);
            LabView.Release(computer); LabView.Release(project); LabView.Release(app); lease.Dispose();
        }
    }
}
}
