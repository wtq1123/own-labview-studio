using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace OwnLabView {
public static class Data {
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 64 };
    public static Dictionary<string, object> Map(object value) {
        var map = value as Dictionary<string, object>;
        if (map == null) throw new ArgumentException("Expected a JSON object.");
        return map;
    }
    public static object[] Array(object value) {
        var array = value as object[];
        if (array == null) throw new ArgumentException("Expected a JSON array.");
        return array;
    }
    public static string Text(Dictionary<string,object> map, string key) {
        object value;
        if (!map.TryGetValue(key, out value) || !(value is string) || String.IsNullOrWhiteSpace((string)value))
            throw new ArgumentException("Expected a non-empty string: " + key);
        return (string)value;
    }
    public static void Keys(Dictionary<string,object> map, params string[] allowed) {
        foreach (string key in map.Keys) if (!allowed.Contains(key)) throw new ArgumentException("Unknown field: " + key);
    }
    public static double Number(object value) {
        if (!(value is int || value is long || value is decimal || value is double || value is float)) throw new ArgumentException("Expected a finite number.");
        double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        if (Double.IsNaN(number) || Double.IsInfinity(number)) throw new ArgumentException("Expected a finite number.");
        return number;
    }
    public static string Error(Exception ex) {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex.GetType().Name + ": " + ex.Message;
    }
    public static string Hash(string path) {
        using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}

public sealed class Node {
    public string Id, Kind, Label;
    public double Value;
    public int Level;
    public string[] Inputs { get { return Kind == "indicator" ? new[] { "in" } : IsMath ? new[] { "a", "b" } : new string[0]; } }
    public bool IsMath { get { return new[] { "add", "subtract", "multiply", "divide" }.Contains(Kind); } }
}
public sealed class Wire { public string From, To; }
public sealed class Graph {
    public string Name;
    public object Source;
    public readonly Dictionary<string, Node> Nodes = new Dictionary<string,Node>(StringComparer.Ordinal);
    public readonly List<Node> Order = new List<Node>();
    public readonly List<Wire> Wires = new List<Wire>();
    public readonly Dictionary<string,string> Incoming = new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly Regex Identifier = new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    public static Graph Parse(object value) {
        var source = Data.Map(value);
        Data.Keys(source, "schema_version", "name", "nodes", "wires");
        if (!source.ContainsKey("schema_version") || !(source["schema_version"] is int) || (int)source["schema_version"] != 1)
            throw new ArgumentException("schema_version must be integer 1.");
        var graph = new Graph { Source = value, Name = Data.Text(source, "name") };
        if (!Identifier.IsMatch(graph.Name)) throw new ArgumentException("name must start with an ASCII letter and contain at most 40 letters, digits or underscores.");
        if (Regex.IsMatch(graph.Name, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase)) throw new ArgumentException("Reserved Windows name.");
        if (!source.ContainsKey("nodes") || !source.ContainsKey("wires")) throw new ArgumentException("nodes and wires are required.");
        var nodes = Data.Array(source["nodes"]); var wires = Data.Array(source["wires"]);
        if (nodes.Length < 1 || nodes.Length > 200 || wires.Length > 500) throw new ArgumentException("Plan allows 1-200 nodes and at most 500 wires.");
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (object obj in nodes) {
            var n = Data.Map(obj); Data.Keys(n, "id", "kind", "label", "value");
            var node = new Node { Id = Data.Text(n,"id"), Kind = Data.Text(n,"kind") };
            if (!Identifier.IsMatch(node.Id) || graph.Nodes.ContainsKey(node.Id)) throw new ArgumentException("Invalid or duplicate node id: " + node.Id);
            if (!new[] { "input", "constant", "add", "subtract", "multiply", "divide", "indicator" }.Contains(node.Kind)) throw new ArgumentException("Unsupported node kind: " + node.Kind);
            node.Label = n.ContainsKey("label") ? Data.Text(n,"label") : node.Id;
            if (node.Label.Length > 80 || node.Label.Any(Char.IsControl)) throw new ArgumentException("Label must be at most 80 characters without control characters.");
            if ((node.Kind == "input" || node.Kind == "indicator") && !labels.Add(node.Label)) throw new ArgumentException("Front panel labels must be unique: " + node.Label);
            if (n.ContainsKey("value")) {
                if (!(node.Kind == "input" || node.Kind == "constant")) throw new ArgumentException("Only inputs and constants accept value.");
                node.Value = Data.Number(n["value"]);
            } else if (node.Kind == "constant") throw new ArgumentException("Constant needs value: " + node.Id);
            graph.Nodes.Add(node.Id, node);
        }
        if (!graph.Nodes.Values.Any(n => n.Kind == "indicator")) throw new ArgumentException("At least one indicator is required.");
        foreach (object obj in wires) {
            var w = Data.Map(obj); Data.Keys(w,"from","to");
            var wire = new Wire { From = Data.Text(w,"from"), To = Data.Text(w,"to") };
            var from = wire.From.Split('.'); var to = wire.To.Split('.');
            if (from.Length != 2 || to.Length != 2 || !graph.Nodes.ContainsKey(from[0]) || !graph.Nodes.ContainsKey(to[0])) throw new ArgumentException("Wire endpoint must be node.port for an existing node.");
            if (from[1] != "out" || graph.Nodes[from[0]].Kind == "indicator") throw new ArgumentException("Invalid source port: " + wire.From);
            if (!graph.Nodes[to[0]].Inputs.Contains(to[1])) throw new ArgumentException("Invalid destination port: " + wire.To);
            if (graph.Incoming.ContainsKey(wire.To)) throw new ArgumentException("Input has multiple drivers: " + wire.To);
            graph.Incoming.Add(wire.To,from[0]); graph.Wires.Add(wire);
        }
        foreach (Node node in graph.Nodes.Values) foreach (string port in node.Inputs)
            if (!graph.Incoming.ContainsKey(node.Id+"."+port)) throw new ArgumentException("Unwired required input: " + node.Id+"."+port);
        var remaining = graph.Nodes.Values.ToList(); var seen = new HashSet<string>();
        while (remaining.Count > 0) {
            var ready = remaining.Where(n => n.Inputs.All(p => seen.Contains(graph.Incoming[n.Id+"."+p]))).ToList();
            if (ready.Count == 0) throw new ArgumentException("Feedback/cyclic graphs require state semantics and are not supported.");
            foreach (var node in ready) {
                node.Level = node.Inputs.Length == 0 ? 0 : 1+node.Inputs.Max(p => graph.Nodes[graph.Incoming[node.Id+"."+p]].Level);
                seen.Add(node.Id); graph.Order.Add(node); remaining.Remove(node);
            }
        }
        return graph;
    }
    public object Evaluate(Dictionary<string,object> inputs) {
        foreach (string id in inputs.Keys) if (!Nodes.ContainsKey(id) || Nodes[id].Kind != "input") throw new ArgumentException("Unknown input node: " + id);
        var values = new Dictionary<string,double>(); var outputs = new Dictionary<string,double>();
        foreach (Node n in Order) {
            double value;
            if (n.Inputs.Length == 0) value = inputs.ContainsKey(n.Id) ? Data.Number(inputs[n.Id]) : n.Value;
            else {
                double a = values[Incoming[n.Id+"."+n.Inputs[0]]];
                double b = n.Inputs.Length == 2 ? values[Incoming[n.Id+".b"]] : 0;
                if (n.Kind == "divide" && b == 0) throw new ArithmeticException("Division by zero at " + n.Id);
                switch(n.Kind) {
                    case "add": value=a+b; break; case "subtract": value=a-b; break;
                    case "multiply": value=a*b; break; case "divide": value=a/b; break;
                    default: value=a; break;
                }
            }
            if (Double.IsInfinity(value) || Double.IsNaN(value)) throw new ArithmeticException("Non-finite result at " + n.Id);
            values[n.Id] = value;
            if (n.Kind == "indicator") outputs[n.Id]=value;
        }
        return new { mode="plan_evaluation_only", labview_executed=false, outputs, node_values=values };
    }
    public object Summary() { return new { valid=true, name=Name, node_count=Nodes.Count, wire_count=Wires.Count, execution_order=Order.Select(n=>n.Id).ToArray(), limitations=new[] { "Scalar numeric acyclic plans only.", "Validation does not prove LabVIEW generation or runtime behavior." } }; }
}

public static class Storage {
    public static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
    public static void NoReparse(string path) {
        string current = Path.GetFullPath(path);
        while (!String.IsNullOrEmpty(current)) {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new UnauthorizedAccessException("Output path contains a reparse point: " + current);
            current=Path.GetDirectoryName(current);
        }
    }
    public static string NewProject(Graph graph) {
        string project = Path.GetFullPath(Path.Combine(Root,"projects",graph.Name+"_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8)));
        string prefix = Path.GetFullPath(Path.Combine(Root,"projects"))+Path.DirectorySeparatorChar;
        if (!project.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Project escaped application projects folder.");
        NoReparse(project);
        Directory.CreateDirectory(project);
        return project;
    }
    public static void WriteNew(string path, object value) {
        NoReparse(path);
        using(var file = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer = new StreamWriter(file,new UTF8Encoding(false))) writer.Write(Data.Json.Serialize(value));
    }
    public static string ArtifactFilename(object value,string extension) {
        string filename=value as string;
        if(String.IsNullOrWhiteSpace(filename)||filename.Length>120||filename.Any(Char.IsControl)||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0||filename.Contains(":")||filename!=filename.Trim()||filename.EndsWith(".",StringComparison.Ordinal)||Path.GetFileName(filename)!=filename)
            throw new ArgumentException("filename must be a single local filename of at most120 characters.");
        if(!String.Equals(Path.GetExtension(filename),extension,StringComparison.OrdinalIgnoreCase)||Path.GetFileNameWithoutExtension(filename).Length==0)
            throw new ArgumentException("filename must end in "+extension+" and contain a nonempty stem.");
        string first=filename.Split('.')[0];
        if(System.Text.RegularExpressions.Regex.IsMatch(first,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("filename is a reserved Windows name.");
        return filename;
    }
    public static object Save(Graph graph) {
        string folder=NewProject(graph), path=Path.Combine(folder,"graph.json");
        WriteNew(path,graph.Source);
        return new { saved=true,path,sha256=Data.Hash(path),vi_generated=false };
    }
}

public static class LabView {
    sealed class UnexpectedActivationException:InvalidOperationException {
        public UnexpectedActivationException():base("COM activation unexpectedly started another LabVIEW process; inspection stopped."){}
    }
    static object Active() {
        try { return Marshal.GetActiveObject("LabVIEW.Application"); }
        catch(COMException ex) {
            if(ex.HResult!=unchecked((int)0x800401E3))throw;
            // NI's server may not publish a running-object-table entry. CoCreateInstance
            // can still bind its existing singleton. Only allow that fallback when the
            // registered executable is already running, and verify no new PID appeared.
            var type=Type.GetTypeFromProgID("LabVIEW.Application",true);
            string executable=RegisteredExecutable();
            var before=MatchingProcessIds(executable);
            if(before.Count==0)throw new InvalidOperationException("Start NI LabVIEW first. No matching registered LabVIEW instance is running.");
            object app=Activator.CreateInstance(type);
            if(MatchingProcessIds(executable).Any(id=>!before.Contains(id))) {Release(app);throw new UnexpectedActivationException();}
            return app;
        }
    }
    public static string RegisteredExecutable() {
        var type=Type.GetTypeFromProgID("LabVIEW.Application",true);
        string registered;
        using(var key=Registry.ClassesRoot.OpenSubKey("CLSID\\{"+type.GUID.ToString()+"}\\LocalServer32"))registered=key==null?null:Convert.ToString(key.GetValue(null));
        if(String.IsNullOrWhiteSpace(registered))throw new InvalidOperationException("LabVIEW local COM server path is not registered.");
        int end=registered.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);
        if(end<0)throw new InvalidOperationException("Unrecognized LabVIEW local COM server path.");
        return Path.GetFullPath(registered.Substring(0,end+4).Trim().Trim('"'));
    }
    static List<int> MatchingProcessIds(string executable) {
        var ids=new List<int>();
        foreach(var process in Process.GetProcessesByName("LabVIEW"))using(process)try {if(String.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))ids.Add(process.Id);}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        return ids;
    }
    public static object Invoke(object obj,string method,BindingFlags flags,params object[] args) { return obj.GetType().InvokeMember(method,flags,null,obj,args); }
    public static void Release(object obj) { if (obj!=null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    public static string VerifyHelperPath(object vi,string expectedHash) {
        string actual=Path.GetFullPath(Convert.ToString(Invoke(vi,"Path",BindingFlags.GetProperty)));
        string folder=Path.GetDirectoryName(actual);
        var allowed=new[]{Path.Combine(Storage.Root,"helpers"),Path.Combine(Storage.Root,"recipes","helpers"),Path.Combine(Storage.Root,"type-helpers")}.Select(Path.GetFullPath);
        if(!allowed.Contains(folder,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("LabVIEW resolved an identically named helper outside this application: "+actual+". Finish existing work and restart LabVIEW before retrying.");
        Storage.NoReparse(actual);
        if(Data.Hash(actual)!=expectedHash)throw new IOException("Loaded helper does not match its audited hash: "+actual);
        return actual;
    }
    internal static object ActiveForInspection() { return Active(); }
    public static object Status() {
        object app=null;
        bool registered=Type.GetTypeFromProgID("LabVIEW.Application",false)!=null;
        try {
            app=Active();
            return new { registered,connected=true,version=Convert.ToString(Invoke(app,"Version",BindingFlags.GetProperty)),started_labview=false,ran_vi=false };
        } catch(Exception ex) { return new { registered,connected=false,started_labview=ex is UnexpectedActivationException,ran_vi=false,error=Data.Error(ex),action="Start LabVIEW normally, then retry. Inspection stops if COM starts a new process instead of binding the existing one." }; }
        finally { Release(app); }
    }
    public static object Inspect(string path) {
        if (!Path.IsPathRooted(path)) throw new ArgumentException("VI path must be absolute.");
        path=Path.GetFullPath(path);
        if (!File.Exists(path) || !path.EndsWith(".vi",StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Existing .vi file required.",path);
        string before=Data.Hash(path); object app=null,vi=null;
        try {
            app=Active(); vi=Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);
            var name=Invoke(vi,"Name",BindingFlags.GetProperty); var state=Invoke(vi,"ExecState",BindingFlags.GetProperty);
            string after=Data.Hash(path);
            if (before!=after) throw new IOException("VI changed on disk during inspection; cause unknown.");
            return new { path,name,execution_state=state,sha256=after,file_unchanged=true,ran_vi=false,saved_vi=false,scope="Basic VI metadata only; this is not block-diagram inspection." };
        } finally { Release(vi); Release(app); }
    }
}

public static class Service {
    public const string Version="1.1.0";
    public static readonly string Sample="{\"schema_version\":1,\"name\":\"AdditionDemo\",\"nodes\":[{\"id\":\"a\",\"kind\":\"input\",\"label\":\"A\",\"value\":2},{\"id\":\"b\",\"kind\":\"input\",\"label\":\"B\",\"value\":3},{\"id\":\"sum\",\"kind\":\"add\"},{\"id\":\"result\",\"kind\":\"indicator\",\"label\":\"Result\"}],\"wires\":[{\"from\":\"a.out\",\"to\":\"sum.a\"},{\"from\":\"b.out\",\"to\":\"sum.b\"},{\"from\":\"sum.out\",\"to\":\"result.in\"}]}";
    public static object Capabilities() { bool accepted=CompletionStatus.Accepted();return new { application="Own LabVIEW Studio",version=Version,complete_replacement=accepted,acceptance_scope="The extracted29 VI Generator contracts plus the agreed own class/interface and local My Computer project/library/build extensions, using native typical and boundary fixtures; not exhaustive LabVIEW API coverage or original-vendor runtime comparison",acceptance_report=CompletionStatus.Report,runtime_dependencies=new[]{"Windows .NET Framework4.x x86","NI LabVIEW2024 x86 development environment; Application Builder for builds","Pinned own helper VIs and MIT lv-scripting authoring components"},vendor_runtime_required=false,native_helpers_present=Native.Available,root=Storage.Root,supported=new[]{"Windows desktop and Codex MCP share the same backend; no window automation required","Native VI creation, copy editing, node/control/structure placement and wiring with scoped symbolic references","Numeric representations, Boolean text/styles, arrays, clusters, defaults, properties/methods, loop registers, tunnels, Formula and Named Cluster operations","Automatic connector assignment and reassignment; selected independent/NI file preparation and root/loop/Case placement","Merge preserves accepted native type/default/topology/geometry domains, complex scalars and recursive containers; source-prefix names resolve copied label collisions","Own classes with inheritance, static/dynamic accessors, custom same-signature overrides and normal-VI method dependency snapshots","Class private fields and CTL/globals: scalar, nested cluster, rectangular1..8-dimensional scalar/cluster arrays with persisted defaults","Genuine interfaces, interface inheritance, multiple implemented interfaces and native default implementations","Native mixed/nested project libraries, isolated copying and member edits with qualified-name/compiler checks","Local My Computer projects with VI/CTL/global/class/library members, nested virtual folders and isolated copy editing","Create, copy and modify EXE/DLL/SDIST build specifications; optional real Application Builder outputs in fresh destinations","Standalone and MCP native acceptance preserve sources and never run hardware or measurement VIs"},pending=accepted?new string[0]:new[]{"Final current-binary regression and migration acceptance"},scope_exclusions=new[]{"RT/FPGA and hardware execution","General-purpose Agent For LabVIEW tools are provided by Codex","Exhaustive arbitrary NI property/type/style domains, opaque reference values, FP decoration/pane redesign and source connector migration during merge","Desktop visual interaction was excluded by the user's no-window requirement","Generic VI generation does not execute the resulting program"},maintenance_limitations=new[]{"Some helpers historically used vendor authoring tools; the production runtime does not depend on those tools. Full helper-source bootstrap is not an acceptance requirement."},legacy_directories_present=CompletionStatus.LegacyPresent(),deletion_gate=accepted?"Functional/migration gate passed for the agreed scope; consult the deletion receipt for physical cleanup status.":"BLOCKED until the current production files match the complete acceptance receipt." }; }
    public static object Call(string name,Dictionary<string,object> args) {
        switch(name) {
            case "studio_capabilities": Data.Keys(args); return Capabilities();
            case "create_library": return Libraries.Create(args);
            case "create_project": return Projects.Create(args);
            case "import_vi": return ViImport.Import(args);
            case "merge_vi": return ViMerge.Merge(args);
            case "create_type_artifact": return TypeArtifacts.Create(args);
            case "place_ni_file": return NiFileObjects.Place(args);
            case "prepare_file": return TypeImport.Prepare(args);
            case "search_styles": return Discovery.SearchStyles(args);
            case "find_ni_examples": return Discovery.FindExamples(args);
            case "validate_graph": Data.Keys(args,"graph"); return Graph.Parse(Required(args,"graph")).Summary();
            case "evaluate_graph": Data.Keys(args,"graph","inputs"); return Graph.Parse(Required(args,"graph")).Evaluate(args.ContainsKey("inputs")?Data.Map(args["inputs"]):new Dictionary<string,object>());
            case "save_graph": Data.Keys(args,"graph"); return Storage.Save(Graph.Parse(Required(args,"graph")));
            case "labview_status": Data.Keys(args); return LabView.Status();
            case "inspect_vi": Data.Keys(args,"path"); return LabView.Inspect(Data.Text(args,"path"));
            case "export_vi_html": return BasicViTools.Export(args);
            case "auto_wire_copy": return BasicViTools.WireCopy(args);
            case "build_vi": Data.Keys(args,"graph","verify"); if(args.ContainsKey("verify") && !(args["verify"] is bool))throw new ArgumentException("verify must be boolean.");return Native.Build(Graph.Parse(Required(args,"graph")),args.ContainsKey("verify")&&(bool)args["verify"]);
            case "recipe_catalog": Data.Keys(args); return Recipe.Catalog();
            case "validate_recipe": Data.Keys(args,"recipe"); return Recipe.Validate(Required(args,"recipe"));
            case "build_recipe": Data.Keys(args,"recipe"); return Recipe.Build(Required(args,"recipe"));
            default: throw new ArgumentException("Unknown tool: "+name);
        }
    }
    static object Required(Dictionary<string,object> args,string key) { if(!args.ContainsKey(key))throw new ArgumentException("Missing argument: "+key);return args[key]; }
    public static object[] Tools() {
        var graph=new { type="object",description="schema_version=1; name; nodes (input/constant/add/subtract/multiply/divide/indicator); wires using node.port. Inputs of arithmetic are a,b; indicator is in; source is out. All fields strictly checked." };
        return new object[] {
            Tool("studio_capabilities","Report capabilities, current production-hash acceptance and legacy directory status.",new{},new string[0],true),
            Tool("export_vi_html","Export NI VI documentation with front-panel and diagram PNGs into a new or empty directory. Attaches only to running NI; never saves or executes the source. Source hash and actual loaded path checked.",new{path=new{type="string"},output_html=new{type="string"}},new[]{"path","output_html"},false),
            Tool("auto_wire_copy","Connect two uniquely labelled top-level objects in a new VI disk copy. Front-panel control terminal index is 0; Node indexes use native order. Requires equal terminal types, source/sink direction, same root diagram and an unwired sink. Reports wire integrity and compilation separately. Never saves or runs source or generated VI. Rejects non-NI static callees; partial failed copies retained.",new{source_vi=new{type="string"},name=new{type="string"},source_object=new{type="string"},source_terminal=new{type="integer",minimum=0,maximum=255},sink_object=new{type="string"},sink_terminal=new{type="integer",minimum=0,maximum=255}},new[]{"source_vi","name","source_object","source_terminal","sink_object","sink_terminal"},false),
            Tool("place_ni_file","Place one of two exactly hashed NI candidates into a new isolated VI: category=polymorphic selects Little-Endian Bytes To Words Poly.vi; category=class selects a Less Comparable.lvclass constant. Optional source_path must equal that category's reviewed path. Reads actual native class and terminal types, current callee/typed-library dependencies, saves and freshly reopens in separate workers. Preserves source bytes; neither source, class methods nor generated VI is run. Arbitrary external or inherited classes are excluded. Optional filename selects the new .vi filename; x/y are Int16 diagram coordinates.",new{name=new{type="string"},category=new{type="string",@enum=new[]{"polymorphic","class"}},source_path=new{type="string"},filename=new{type="string"},x=new{type="integer",minimum=Int16.MinValue,maximum=Int16.MaxValue},y=new{type="integer",minimum=Int16.MinValue,maximum=Int16.MaxValue}},new[]{"name","category"},false),
            Tool("prepare_file","Prepare an explicitly selected independent .vi or .ctl in a new own project. Reads actual native VIType to distinguish subVI, polymorphic VI, global and control; copies independent file bytes and audits actual static NI/own dependency paths and physical carrier hashes. Selected NI polymorphic VIs retain their original compiled NI path and instance-carrier identity. Relocated executable/poly copies must remain compiled idle. Source and copy are never Run or source Save. Returns prepared vi_path for file_place recipes using a current owned root/loop/Case diagram and symbolic terminal outputs. Selected NI classes retain their pinned original library path and actual native member/static-dependency evidence. Independent external classes are copied as complete class/VI/CTL/VIM directory snapshots preserving basenames and relative paths; absolute local XML Item URLs are relocated only in the copy. Snapshot class/member actual Path/type, static callees and all copied file hashes must pass native checks before recipe use. Original external classes are never opened, Run or Save by preparation. Native parent and interface paths are audited recursively and copied hierarchy hashes are pinned. Known vendor paths and static dependencies outside NI or pinned own assets are rejected with their paths. Does not invoke prepare within a recipe worker.",new{source_path=new{type="string"},name=new{type="string"}},new[]{"source_path","name"},false),
            Tool("create_type_artifact","Create a .ctl, global .vi, or genuine own .lvclass using NI providers. kind=class supports0..32 private fields: numeric(DBL)/Boolean/String, nested clusters, and rectangular1..8-dimensional scalar/cluster arrays, including defaults; optional static_members contains0..16 {source_path,name} copies of compiled standalone own normal VIs with no subVI dependencies. Original member files are preserved. Optional parent_class snapshots an app-authored class hierarchy. accessors contains {field,dispatch=static|dynamic,include_error_terminals}; each creates read/write VIs. overrides contains {method,result_values?} for an inherited dynamic accessor; optional DBL/Boolean/String output replacements customize the copied child only. methods accepts0..16 {name,source_path,dispatch=dynamic|static}: wraps an executable own VI with assigned connector pane and NI or owned normal-VI dependencies; snapshots and relinks the body closure and retains input defaults. Connector labels/types are retained with self in/out added; data inputs are required. Custom overrides use {method,source_path} and require the same native signature. No generated method is run. is_interface=true creates a native interface without fields/class parent/accessors; interfaces lists up to8 own parent interfaces for inheritance or implementation. Supplied dynamic interface methods have native default implementations (must_override=false). kind=control requires one root; kind=global allows1..32. Node fields: kind(numeric/boolean/string/cluster/array), label, optional x/y, children for clusters, element for arrays (numeric/boolean/string/cluster), dimensions=1..8, optional root value. Arrays are rectangular with at most1024 leaf default elements; control trees have at most128 nodes and depth8. Nested defaults are supplied as the complete root value. Complex dynamic accessors and parent-forwarding overrides are supported; result_values remains scalar only. Optional control_vi_type accepts the NI enum1..3 on control artifacts. Optional filename selects the .ctl/.vi filename inside a unique new app-owned project directory. Reads back classes, labels, defaults and VI type; never runs the generated artifact.",new{name=new{type="string"},filename=new{type="string"},kind=new{type="string",@enum=new[]{"control","global","class"}},controls=new{type="array",items=new{type="object"}},static_members=new{type="array",maxItems=16,items=new{type="object"}},methods=new{type="array",maxItems=16,items=new{type="object"}},parent_class=new{type="string"},is_interface=new{type="boolean"},interfaces=new{type="array",maxItems=8,items=new{type="string"}},accessors=new{type="array",maxItems=32,items=new{type="object"}},overrides=new{type="array",maxItems=16,items=new{type="object"}},control_vi_type=new{type="integer",minimum=1,maximum=3}},new[]{"name","kind","controls"},false),
            Tool("import_vi","Copy an explicitly selected standalone local VI to a new application project and inspect/save that copy without running it. Preserves the source bytes. Accepts only a compiled VI with static VI callees under the registered NI vi.lib; non-NI callees are rejected. This is not an audit of DLLs, dynamic calls or runtime behavior. Successful copies can be used as recipe source_vi or subvi inputs.",new{source_path=new{type="string"},name=new{type="string"}},new[]{"source_path","name"},false),
            Tool("merge_vi","Duplicate the complete diagram selection (objects and wires) and reviewed scalar, rank1/2 DBL/Boolean/String Array or scalar/mixed/nested Cluster FP controls into a unique copy of the target. App-generated, app-copied, NI-only app-imported and prior-merge artifacts require matching immutable own VI/report/plan ancestry, recursively bounded to16 levels and64 artifacts. Copied collisions, duplicate and blank labels receive a source prefix with a reported mapping; actual FP/BD identities, types, defaults, positions, bounds, frame containment, wire topology and saved NI/app static dependencies checked. Finite numeric Slide/progress controls, root BD decorations and Case/Event input tunnels accepted. Optional filename chooses the output filename; dx/dy are Int32 diagram offsets, default0. Native descriptors support complex scalars, recursive known clusters, scalar arrays and rank1 cluster arrays. Opaque types, unobserved array element shapes, FP decorations/multiple panes and source connector migration remain excluded. Existing target connector assignments are preserved. Never runs either input or the generated merged VI.",new{source_path=new{type="string"},target_source_path=new{type="string"},name=new{type="string"},filename=new{type="string"},dx=new{type="integer",minimum=Int32.MinValue,maximum=Int32.MaxValue},dy=new{type="integer",minimum=Int32.MinValue,maximum=Int32.MaxValue}},new[]{"source_path","target_source_path","name"},false),
            Tool("create_library","Create an isolated native project library from copied own VI, CTL, global, class or library members. source_library copies a previously audited own library; member_paths adds copied members, remove_members removes exact direct member names in the copy. Source files are preserved. Native qualified names, compilation and local/NI dependency boundaries are checked; generated VIs are never run.",new{name=new{type="string"},member_paths=new{type="array",items=new{type="string"}},source_library=new{type="string"},remove_members=new{type="array",items=new{type="string"}}},new[]{"name"},false),
            Tool("create_project","Create/copy-edit a local My Computer project. vi_paths is the complete desired own/prepared VI/CTL/global/class/library member list. folders lists virtual slash-separated folder paths; member_folders maps absolute member paths to folder paths. Layout and build specs inherit from source_project unless explicitly supplied. build_specs contains {name,kind=EXE|DLL|SDIST,source_path,output_name?}; each source is a requested ordinary VI. build=true actually generates fresh isolated output files. Original project, source files and outputs are preserved; generated VIs are not run. RT/FPGA excluded.",new{name=new{type="string"},vi_paths=new{type="array",items=new{type="string"}},source_project=new{type="string"},folders=new{type="array",items=new{type="string"}},member_folders=new{type="object"},build_specs=new{type="array",maxItems=16,items=new{type="object"}},build=new{type="boolean"}},new[]{"name","vi_paths"},false),
            Tool("search_styles","Search the local NI node/control style index by exact, prefix and substring match. Results are metadata candidates, not per-style generation acceptance.",new{query=new{type="string"},limit=new{type="integer",minimum=1,maximum=100}},new[]{"query"},true),
            Tool("find_ni_examples","Browse direct VI/subfolder names when query is omitted or empty; follow next_page_args for stable pagination. A nonempty query keeps bounded recursive name search. Reports partial scans and opaque LLB containers; never calls LabVIEW.",new{query=new{type="string"},relative_folder=new{type="string"},limit=new{type="integer",minimum=1,maximum=100},offset=new{type="integer",minimum=0,maximum=10000},snapshot_id=new{type="string",pattern="^[0-9a-f]{64}$"}},new string[0],true),
            Tool("validate_graph","Validate scalar numeric graph structure, ports, drivers, complete wiring and absence of cycles. Does not generate or run a VI.",new{graph},new[]{"graph"},true),
            Tool("evaluate_graph","Evaluate the validated arithmetic plan in this application. This is not LabVIEW execution or LabVIEW acceptance.",new{graph,inputs=new{type="object",description="Optional numeric overrides keyed by input node id."}},new[]{"graph"},true),
            Tool("save_graph","Save a validated graph JSON in a new unique projects folder. Does not create a VI.",new{graph},new[]{"graph"},false),
            Tool("labview_status","Attach to an already-running NI LabVIEW and read its version. Does not start LabVIEW or run a VI.",new{},new string[0],true),
            Tool("inspect_vi","Read basic metadata for a local VI using an existing LabVIEW instance. May load the VI and dependencies into memory; never invokes Run or Save.",new{path=new{type="string"}},new[]{"path"},true),
            Tool("build_vi","Create a new isolated scalar numeric DAG VI using NI LabVIEW and five audited user-authored helpers. Input/arithmetic/indicator only; constants currently rejected. May start LabVIEW. verify=true runs only the new finite arithmetic VI and compares its real outputs. Helper hashes and empty callees are checked before execution.",new{graph,verify=new{type="boolean",description="Default false. Execute only the generated scalar arithmetic VI and compare outputs."}},new[]{"graph"},false),
            Tool("recipe_catalog","List native helper recipe operations and exact input/output contracts. Catalog presence is not proof of feature acceptance.",new{},new string[0],true),
            Tool("validate_recipe","Validate an ordered native recipe using symbolic references from prior steps. Does not create or run a VI.",new{recipe=new{type="object",description="schema_version=1, name, steps; optional source_vi must be a previously generated application VI with matching provenance. Read recipe_catalog for operations and types."}},new[]{"recipe"},true),
            Tool("build_recipe","Create and save a new isolated VI or edit a new disk copy of an audited application-generated source_vi. May start NI LabVIEW; generic recipes never run the generated VI. Returns saved artifact, source/dependency/hash evidence and available compilation/wire checks.",new{recipe=new{type="object"}},new[]{"recipe"},false)
        };
    }
    static object Tool(string name,string description,object properties,string[] required,bool readOnly) {
        return new {name,description,inputSchema=new{type="object",properties,required,additionalProperties=false},annotations=new{readOnlyHint=readOnly,destructiveHint=false,idempotentHint=readOnly,openWorldHint=false}};
    }
}
}
