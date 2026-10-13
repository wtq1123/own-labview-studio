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
// Recipe operations always act on a newly created isolated VI or disk copy. Reference values
// come only from earlier steps of this worker, never from caller-supplied numbers.
public static class Recipe {
    static readonly Regex Id = new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    static readonly string[] NumericRepresentationNames={"EXT","DBL","SGL","I32","I16","I8","U32","U16","U8","CEXT","CDBL","CSGL","I64","U64","FXP"};
    static readonly int[] NumericDescriptorCodes={0x0B,0x0A,0x09,0x03,0x02,0x01,0x07,0x06,0x05,0x0E,0x0D,0x0C,0x04,0x08,0x5F};
    static Dictionary<string,object> CatalogMap() {return Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","operations.json"),Encoding.UTF8)));}
    public static object Catalog() {return new {operations=CatalogMap(),execution="Build and save only; generic recipes never run the generated VI.",reference_syntax="$step.output or $step.output.0; $root.diagram_id is the new top-level diagram",complete_replacement=false};}
    public static object Validate(object raw) {
        var doc=Data.Map(raw);Data.Keys(doc,"schema_version","name","steps","source_vi","filename");
        if(doc.ContainsKey("filename"))Storage.ArtifactFilename(doc["filename"],".vi");
        var inputDependencyPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(doc.ContainsKey("source_vi"))inputDependencyPaths.Add(GeneratedViDependency.ValidateOne(Data.Text(doc,"source_vi")).Path);
        if(!doc.ContainsKey("schema_version")||Data.Number(doc["schema_version"])!=1)throw new ArgumentException("recipe.schema_version must be 1");
        string name=Data.Text(doc,"name");if(!Id.IsMatch(name))throw new ArgumentException("Invalid recipe name");
        var steps=Data.Array(doc["steps"]);if(steps.Length==0||steps.Length>500)throw new ArgumentException("Recipe needs 1..500 steps");
        var catalog=CatalogMap();var validationStyles=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","ni-styles.json"),Encoding.UTF8)));var seen=new HashSet<string>(StringComparer.Ordinal){"root"};int totalGrowth=0;bool defaultsStarted=false;
        foreach(object item in steps) {
            var step=Data.Map(item);Data.Keys(step,"id","op","args");string id=Data.Text(step,"id"),op=Data.Text(step,"op");
            if(!Id.IsMatch(id)||seen.Contains(id))throw new ArgumentException("Duplicate/invalid step id: "+id);
            if(!catalog.ContainsKey(op))throw new ArgumentException("Unsupported operation: "+op);
            if(defaultsStarted&&op!="control_default"&&op!="array_default"&&!new[]{"get_number","get_string","is_indicator","object_info","control_terminal","representation_info","connector_info","named_bundle_info","named_unbundle_info","array_info","for_stop_if_true_info","while_stop_if_true_info","tunnel_conditional_info","boolean_text_info","node_label_info","for_count_info","case_selector_info"}.Contains(op))throw new ArgumentException("Place defaults after all graph edits; subsequent steps may only set defaults or inspect values.");
            if(op=="control_default"||op=="array_default")defaultsStarted=true;
            var contract=Data.Map(catalog[op]);var signature=Data.Map(contract["inputs"]);var args=Data.Map(step["args"]);
            bool childRepresentation=op=="cluster_child"&&args.ContainsKey("representation");
            var optional=contract.ContainsKey("optional_inputs")?Data.Map(contract["optional_inputs"]):new Dictionary<string,object>();
            if(args.Keys.Any(k=>!signature.ContainsKey(k)&&!optional.ContainsKey(k)))throw new ArgumentException("Arguments differ from operation contract: "+op);
            foreach(var field in optional)if(args.ContainsKey(field.Key))Coerce(args[field.Key],Convert.ToString(field.Value),validationStyles);
            foreach(var field in signature) {
                if(!args.ContainsKey(field.Key))throw new ArgumentException("Missing "+field.Key);
                string type=Convert.ToString(field.Value);object value=args[field.Key];
                if(type=="ref") {
                    string reference=value as string;
                    if(reference==null||!reference.StartsWith("$"))throw new ArgumentException("Reference must originate from this recipe: "+field.Key);
                    string[] parts=reference.Substring(1).Split('.');
                    if(parts.Length<2||parts.Any(p=>p.Length==0)||!seen.Contains(parts[0]))throw new ArgumentException("Unknown/forward reference: "+reference);
                } else {
                    object coerced=Coerce(value,type,type=="style"?validationStyles:null);
                    if(type=="generated_vi"||type=="generated_type") {string path=Convert.ToString(coerced);CheckDependencyName(path,inputDependencyPaths);inputDependencyPaths.Add(path);if(inputDependencyPaths.Count>256)throw new ArgumentException("Recipe file dependencies exceed 256 paths.");}
                }
            }
            if(op=="grow") {int count=Convert.ToInt32(args["chunks"]);totalGrowth+=count;if(count<1||count>64||totalGrowth>256)throw new ArgumentException("Grow operations allow 1..64 chunks each and at most 256 chunks in one recipe.");}
            if(op=="property_select") {var selected=Data.Array(args["property_id_strings"]);if(selected.Length<1||selected.Length>64||selected.Any(x=>String.IsNullOrWhiteSpace(Convert.ToString(x))))throw new ArgumentException("Select 1..64 nonempty property IDs.");}
            if(op=="cluster_child") {
                if(!new[]{"Numeric Control","String Control","Cluster","Horizontal Toggle Switch"}.Contains(Data.Text(args,"style")))throw new ArgumentException("Cluster children support verified Digital, String, Boolean and Cluster styles only.");
                if(String.IsNullOrWhiteSpace(Data.Text(args,"label")))throw new ArgumentException("Cluster children require a nonempty field label.");
                if(childRepresentation)ValidateClusterChildRepresentation(Data.Text(args,"style"),args["representation"]);
            }
            if(op=="array_element"&&(!new[]{"Numeric Control","String Control","Horizontal Toggle Switch"}.Contains(Data.Text(args,"style"))||String.IsNullOrWhiteSpace(Data.Text(args,"label"))))throw new ArgumentException("Array elements require Digital, String or Boolean style and a nonempty label.");
            if(op=="array_dimensions"&&(Data.Number(args["dimensions"])<1||Data.Number(args["dimensions"])>2))throw new ArgumentException("Array dimensions must be 1 or 2.");
            if(op=="representation")ValidateNumericRepresentation(args["representation"]);
            if(op=="file_place") {
                NiRecipeFiles.Validate(args);
                string path=ViImport.CanonicalLocalPath(Data.Text(args,"source_path"));CheckDependencyName(path,inputDependencyPaths);inputDependencyPaths.Add(path);
                if(inputDependencyPaths.Count>256)throw new ArgumentException("Recipe file dependencies exceed 256 paths.");
            }
            if(op=="decoration"&&Convert.ToUInt32(args["style"])!=3602)throw new ArgumentException("Decoration currently supports independently tested root Flat Box style 3602 only.");
            if(op=="ctl_constant"||op=="global_node")ValidateTypePlacement(Data.Text(args,"type_path"),op=="global_node",op=="global_node"?Data.Text(args,"control_name"):null);
            if(op=="connector_auto")ValidateConnectorLabels(Data.Array(args["control_labels"]).Select(Convert.ToString).ToArray());
            if(op=="named_bundle_select"||op=="named_unbundle_select") ValidateNamedNames(Data.Array(args["element_names"]).Select(Convert.ToString).ToArray());
            seen.Add(id);
        }
        RecipeReferenceGuard.Validate(doc,catalog);
        return new {valid=true,name,step_count=steps.Length,labview_executed=false,scope="Recipe contracts and conservative reference types/lifetimes only; native helper ABI and VI compilation are checked during generation."};
    }
    static object Coerce(object raw,string type,Dictionary<string,object> styles) {
        if(type=="array_value") {ToTypedArray(raw,null);return raw;}
        if(type=="control_value") {ValidateControlValue(raw);return raw;}
        if(type=="generated_vi") {
            if(!(raw is string))throw new ArgumentException("Generated subVI requires an absolute path");
            return GeneratedViDependency.ValidateOne((string)raw).Path;
        }
        if(type=="generated_type") {
            if(!(raw is string))throw new ArgumentException("Generated type requires an absolute path");
            return GeneratedTypeDependency.ValidateOne((string)raw).Path;
        }
        if(type=="formula_text") {if(!(raw is string)||((string)raw).Length>65536)throw new ArgumentException("Formula text must contain at most 65536 UTF-16 code units");return raw;}
        if(type=="string"||type=="style") {
            if(!(raw is string)||((string)raw).Length>4096)throw new ArgumentException("Expected bounded text");
            if(type=="style"&&styles!=null){if(!styles.ContainsKey((string)raw))throw new ArgumentException("Unknown NI style: "+raw);return Convert.ToUInt32(styles[(string)raw]);}
            return raw;
        }
        if(type=="bool") {if(!(raw is bool))throw new ArgumentException("Expected boolean");return raw;}
        if(type=="short") {double n=Data.Number(raw);if(n!=Math.Truncate(n)||n<0||n>16000)throw new ArgumentException("Coordinate/index must be an integer 0..16000");return (short)n;}
        if(type=="coordinate_i16") {double n=Data.Number(raw);if(n!=Math.Truncate(n)||n<Int16.MinValue||n>Int16.MaxValue)throw new ArgumentException("File coordinate must be an exact Int16");return (short)n;}
        if(type=="uint") {double n=Data.Number(raw);if(n!=Math.Truncate(n)||n<0||n>UInt32.MaxValue)throw new ArgumentException("Expected uint32");return (uint)n;}
        if(type=="number")return Data.Number(raw);
        if(type=="bounds") {var a=Data.Array(raw);if(a.Length!=2)throw new ArgumentException("Bounds are [width,height]");return new object[]{Coerce(a[0],"short",null),Coerce(a[1],"short",null)};}
        if(type=="strings") {var a=Data.Array(raw);if(a.Length>1000||a.Any(v=>!(v is string)||((string)v).Length>4096))throw new ArgumentException("Expected bounded text array");return a.Select(Convert.ToString).ToArray();}
        throw new ArgumentException("Unsupported input type "+type);
    }
    public static object Build(object raw) {
        Validate(raw);string name=Data.Text(Data.Map(raw),"name");
        NiApplicationLifetime.EnsureForBuild();
        string folder=Path.Combine(Storage.Root,"projects",name+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
        Storage.NoReparse(Path.GetDirectoryName(folder));Directory.CreateDirectory(folder);
        string request=Path.Combine(folder,"recipe.json");Storage.WriteNew(request,raw);
        var start=new ProcessStartInfo(Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--recipe-worker \""+request+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Storage.Root};
        var errors=new StringBuilder();
        using(var child=new Process{StartInfo=start}) {
            child.OutputDataReceived+=(s,e)=>{};child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};
            child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
            if(!child.WaitForExit(180000)){child.Kill();child.WaitForExit();throw new TimeoutException("Recipe worker timed out. Partial artifacts retained at "+folder+"; generated VI was never run.");}
            child.WaitForExit();if(child.ExitCode!=0)throw new InvalidOperationException("Recipe failed: "+errors+" Partial project: "+folder);
        }
        string report=Path.Combine(folder,"recipe-report.json");if(!File.Exists(report))throw new IOException("Missing recipe report");
        return Data.Json.DeserializeObject(File.ReadAllText(report,Encoding.UTF8));
    }
    public static object Worker(string request) {
        request=Path.GetFullPath(request);Storage.NoReparse(request);
        if(!request.StartsWith(Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(request)!="recipe.json")throw new UnauthorizedAccessException("Worker accepts isolated recipe.json only");
        object raw=Data.Json.DeserializeObject(File.ReadAllText(request,Encoding.UTF8));Validate(raw);var doc=Data.Map(raw);
        string folder=Path.GetDirectoryName(request),target=Path.Combine(folder,doc.ContainsKey("filename")?Storage.ArtifactFilename(doc["filename"],".vi"):Path.GetFileName(Path.GetDirectoryName(request))+".vi");if(File.Exists(target))throw new IOException("Output exists");
        var catalog=CatalogMap();var steps=Data.Array(doc["steps"]);
        var needed=new HashSet<string>{"Native_Create","Native_GetRootDiagram","Native_GetNodeTerminals","Native_GetTerminalName","Native_GetDiagramWires","Native_GetWireBroken","Native_GetObjectInfo"};
        foreach(object s in steps) {
            string helperName=Data.Text(Data.Map(catalog[Data.Text(Data.Map(s),"op")]),"helper");
            if(helperName=="Own20261013_ScriptingAdd_V2")needed.Add("Own20261012_GetTerminalContext");
            if(helperName=="Managed_FindControl"||helperName=="Managed_ControlDefault"||helperName=="Managed_ArrayDefault") {needed.Add("Native_GetPanelControls");needed.Add("Native_GetControlLabel");needed.Add("Native_GetControlTerminal");if(helperName!="Managed_FindControl")needed.Add("Own20261012_GetControlIndicator");}
            else if(!helperName.StartsWith("Managed_",StringComparison.Ordinal))needed.Add(helperName);
            if(helperName=="Managed_ArrayMoveElement")foreach(string n in new[]{"Own20261012_MoveControlToOwner","Own20261012_GetArrayInfo","Own20261012_GetObjectIdentity","Native_GetPanelControls","Native_GetControlTerminal","Native_GetTerminalTypeDescriptor","Own20261012_GetControlIndicator","Own20261012_SetControlIndicator","Own20261012_GetNumericRepresentation"})needed.Add(n);
            if(helperName=="Managed_BooleanText"||helperName=="Managed_BooleanTextInfo")foreach(string n in new[]{"Own20261012_GetBooleanStyleInfo","Own20261012_GetBooleanText","Native_GetControlLabel","Native_GetControlTerminal","Native_GetPanelControls","Own20261012_GetObjectIdentity","Own20261012_GetControlIndicator"})needed.Add(n);
            if(helperName=="Managed_BooleanText")needed.Add("Own20261012_SetBooleanStateStrings");
            if(helperName=="Managed_NodeLabel"||helperName=="Managed_NodeLabelInfo")foreach(string n in new[]{"Own20261012_PublicGetNodeLabel_v1","Own20261012_PublicGetNodeLabelVisible_v1","Own20261012_GetObjectIdentity"})needed.Add(n);
            if(helperName=="Managed_NodeLabel")foreach(string n in new[]{"Own20261012_PublicSetNodeLabel_v1","Own20261012_PublicSetNodeLabelVisible_v1"})needed.Add(n);
            if(helperName=="Managed_ForCountInfo"||helperName=="Managed_CaseSelectorInfo")foreach(string n in new[]{"Own20261012_GetObjectIdentity","Own20261012_GetStructureInfo","Own20261012_GetTerminalContext","Native_GetTerminalTypeDescriptor"})needed.Add(n);
            if(helperName=="Managed_ForCountInfo")foreach(string n in new[]{"Own20261012_PublicGetForCountSides_v2","Native_GetForTerminals"})needed.Add(n);
            if(helperName=="Managed_CaseSelectorInfo")foreach(string n in new[]{"Own20261012_PublicGetCaseSelectorSides_v2","Native_GetCaseInfo_v2"})needed.Add(n);
            if(helperName=="Managed_ObjectInfo"||helperName=="Managed_RootDecoration")foreach(string n in new[]{"Own20261012_GetObjectIdentity","Own20261012_PublicGetObjectPosition"})needed.Add(n);
            if(helperName=="Managed_RootDecoration")foreach(string n in new[]{"Own20261012_PublicAddRootDecoration_v2","Native_GetDiagramObjects"})needed.Add(n);
            if(helperName=="Managed_ConnectSourceToStructure") {needed.Add("Own20261012_GetStructureInfo");needed.Add("Own20261012_GetTerminalContext");needed.Add("Own20261012_ConnectSourceToStructure");needed.Add("Own20261012_GetObjectIdentity");needed.Add("Native_GetTunnelTerminals_v2");}
            if(helperName=="Managed_ConnectorAuto"||helperName=="Managed_ConnectorInfo") {needed.Add("Own20261012_GetConnectorInfo");needed.Add("Native_GetControlLabel");needed.Add("Own20261012_GetObjectIdentity");needed.Add("Own20261012_GetControlIndicator");}
            if(helperName=="Managed_ConnectorAuto") {needed.Add("Own20261012_PublicDisconnectConnectorAll_v1");needed.Add("Own20261012_SetConnectorPattern");needed.Add("Native_AssignConnectorTerminal");needed.Add("Native_GetPanelControls");needed.Add("Native_GetControlTerminal");}
            if(helperName=="Own20261012_SetDoubleConstantValue") {needed.Add("Native_GetConstantTerminal");needed.Add("Native_GetTerminalTypeDescriptor");needed.Add("Own20261012_GetDigitalConstantValue");}
            if(helperName=="Own20261012_CreateControlReference")foreach(string n in new[]{"Native_GetPanelControls","Own20261012_GetObjectIdentity","Own20261012_PublicGetControlOwner_v1"})needed.Add(n);
            if(helperName=="Native_SetNumericRepresentation"||helperName=="Own20261012_GetNumericRepresentation") {needed.Add("Own20261012_GetNumericRepresentation");needed.Add("Own20261012_PublicGetControlOwner_v1");needed.Add("Own20261012_GetArrayInfo");needed.Add("Own20261012_GetObjectIdentity");needed.Add("Native_GetControlTerminal");needed.Add("Native_GetTerminalTypeDescriptor");}
            if(helperName=="Managed_ArrayDefault"||helperName=="Own20261012_SetArrayDimensions"||Data.Text(Data.Map(s),"op")=="array_element")needed.Add("Own20261012_GetArrayInfo");
            if(helperName=="Managed_ArrayDefault") {needed.Add("Native_GetTerminalTypeDescriptor");needed.Add("Own20261012_GetNumericRepresentation");}
            if(helperName=="Managed_ControlDefault"||helperName=="Managed_ArrayDefault"||Data.Text(Data.Map(s),"op")=="array_element")needed.Add("Own20261012_GetObjectIdentity");
            if(helperName=="Native_AddControl_v2"||helperName=="Own20261012_AddControlToOwner"||helperName=="Native_DeleteGObject")needed.Add("Own20261012_GetObjectIdentity");
            if(Data.Text(Data.Map(s),"op")=="cluster_child"&&Data.Map(Data.Map(s)["args"]).ContainsKey("representation"))foreach(string n in new[]{"Native_SetNumericRepresentation","Own20261012_GetNumericRepresentation"})needed.Add(n);
            if(helperName=="Own20261012_SetForStopIfTrue")needed.Add("Own20261012_GetForStopIfTrue");
            if(helperName=="Own20261012_SetWhileStopIfTrue")needed.Add("Own20261012_GetWhileStopIfTrue");
            if(helperName=="Own20261012_SetLoopTunnelConditional"||helperName=="Own20261012_GetLoopTunnelConditionTerminal")needed.Add("Own20261012_GetLoopTunnelConditional");
            if(helperName=="Own20261012_GetLoopTunnelConditionTerminal")needed.Add("Native_GetTerminalTypeDescriptor");
            if(helperName=="Native_SetStringConstantValue")needed.Add("Native_GetStringConstantValue");
            if(helperName=="Own20261012_SetControlIndicator")needed.Add("Own20261012_GetControlIndicator");
            if(helperName=="Own20261012_SetNamedBundlerElements")needed.Add("Own20261012_GetNamedBundlerElements");
            if(helperName=="Own20261012_SetNamedUnbundlerElements")needed.Add("Own20261012_GetNamedUnbundlerElements");
        }
        GeneratedViDependency copySource=doc.ContainsKey("source_vi")?GeneratedViDependency.ValidateOne(Data.Text(doc,"source_vi")):null;
        var manifest=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","helper-provenance.json"),Encoding.UTF8)));
        var metadata=Data.Map(manifest["helpers"]);
        var stylesRaw=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","ni-styles.json"),Encoding.UTF8)));
        object app=null,vi=null;var refs=new List<object>();var helpers=new Dictionary<string,object>();var values=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);var log=new List<object>();var dependencies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var loadedHelperPaths=new Dictionary<string,string>();
        var generatedDependencies=new Dictionary<string,GeneratedViDependency>(StringComparer.OrdinalIgnoreCase);
        var typeDependencies=new Dictionary<string,GeneratedTypeDependency>(StringComparer.OrdinalIgnoreCase);
        var generatedDependencyPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedSubvis=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedTypes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var structureSourceRequests=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDefaults=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        var clusterMembers=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);var controlClasses=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var arrayElements=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var lease=new NiSessionLease();
        try {
            foreach(string n in needed) {
                string p=Path.Combine(Storage.Root,"recipes","helpers",n+".vi");Storage.NoReparse(p);
                if(!metadata.ContainsKey(n)||Data.Hash(p)!=Data.Text(Data.Map(metadata[n]),"sha256"))throw new IOException("Unverified helper: "+n);
            }
            app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));
            lease.Attached();
            foreach(string n in needed) {
                var h=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,Path.Combine(Storage.Root,"recipes","helpers",n+".vi"),"",false,0);refs.Add(h);helpers[n]=h;
                loadedHelperPaths[n]=LabView.VerifyHelperPath(h,Data.Text(Data.Map(metadata[n]),"sha256"));
                AuditNiDependencies(app,h,refs,dependencies);
            }
            if(copySource==null)InvokeHelper(helpers["Native_Create"],new Dictionary<string,object>{{"output_path",target}},new string[0]);
            else {
                AuditGeneratedDependency(app,copySource.Path,refs,generatedDependencies,typeDependencies,generatedDependencyPaths);
                copySource.VerifyUnchanged();File.Copy(copySource.Path,target,false);copySource.VerifyUnchanged();
            }
            vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,target,"",false,0);refs.Add(vi);
            if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty))),target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("LabVIEW returned a different VI for the new recipe output path");
            values["root"]=InvokeHelper(helpers["Native_GetRootDiagram"],new Dictionary<string,object>{{"target_path",target}},new[]{"diagram_id"});
            foreach(object item in steps) {
                var step=Data.Map(item);string id=Data.Text(step,"id"),op=Data.Text(step,"op");var contract=Data.Map(catalog[op]);var signature=Data.Map(contract["inputs"]);var args=Data.Map(step["args"]);var input=new Dictionary<string,object>();
                foreach(var field in signature)input[field.Key]=Convert.ToString(field.Value)=="ref"?Resolve(Data.Text(args,field.Key),values):Coerce(args[field.Key],Convert.ToString(field.Value),stylesRaw);
                if(op!="cluster_child"&&contract.ContainsKey("optional_inputs"))foreach(var field in Data.Map(contract["optional_inputs"]))if(args.ContainsKey(field.Key))input[field.Key]=Coerce(args[field.Key],Convert.ToString(field.Value),stylesRaw);
                if(contract.ContainsKey("target_path")&&(bool)contract["target_path"])input["target_path"]=target;
                if(op=="cluster_child"&&!new uint[]{3000,3110,3200,3400}.Contains(Convert.ToUInt32(input["style"])))throw new ArgumentException("Cluster child style must resolve to an accepted Digital, Boolean, String or Cluster style.");
                if(op=="array_element") {string ownerKey=ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(input["owner_id"]));if(!new uint[]{3000,3200,3110}.Contains(Convert.ToUInt32(input["style"]))||!arrayElements.ContainsKey(ownerKey)||arrayElements[ownerKey]!=null)throw new ArgumentException("Create exactly one Digital, String or Boolean element inside an empty Array created by this recipe.");}
                string deletedIdentity=op=="delete"&&(clusterMembers.Count!=0||controlClasses.Count!=0||arrayElements.Count!=0)?ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(input["object_id"])):null;
                if(op=="subvi") {
                    string subviPath=Convert.ToString(input["subvi_path"]);requestedSubvis.Add(subviPath);
                    AuditGeneratedDependency(app,subviPath,refs,generatedDependencies,typeDependencies,generatedDependencyPaths);
                }
                string classField=op=="representation"||op=="representation_info"?"numeric_id":op=="set_number"||op=="set_double"||op=="get_number"||op=="set_string"||op=="get_string"?"constant_id":op=="set_index_mode"?"tunnel_id":op=="set_indicator"||op=="is_indicator"||op=="control_default"?"control_id":null;
                string checkedClass=null;
                string genericClass=null;
                if(op=="property_info"||op=="property_class"||op=="property_select") {classField="property_id";genericClass="Property";}
                if(op=="invoke_info"||op=="invoke_class"||op=="invoke_select"||op=="invoke_terminals") {classField="invoke_id";genericClass="Invoke";}
                if(op=="control_reference")ValidateCurrentControlMembership(helpers,target,Convert.ToUInt32(input["control_id"]));
                if(op=="formula_expression"||op=="formula_info"||op=="formula_input"||op=="formula_output") {classField="formula_id";genericClass="Formula";}
                if(op=="formula_parameter_info"||op=="formula_parameter_name"||op=="formula_parameter_delete") {classField="parameter_id";genericClass="FormulaParameter";}
                if(op=="cluster_child") {classField="owner_id";genericClass="Cluster";}
                if(op=="array_element") {classField="owner_id";genericClass="Array";}
                if(op=="array_dimensions"||op=="array_info"||op=="array_default") {classField="array_id";genericClass="Array";}
                if(op=="boolean_text"||op=="boolean_text_info") {classField="boolean_id";genericClass="Boolean";}
                if(op=="for_stop_if_true"||op=="for_stop_if_true_info") {classField="for_id";genericClass="ForLoop";}
                if(op=="while_stop_if_true"||op=="while_stop_if_true_info") {classField="while_id";genericClass="WhileLoop";}
                if(op=="tunnel_conditional"||op=="tunnel_conditional_info"||op=="tunnel_condition_terminal") {classField="tunnel_id";genericClass="LoopTunnel";}
                if(op=="named_bundle_info"||op=="named_bundle_select") {classField="bundler_id";genericClass="NamedBundler";}
                if(op=="named_unbundle_info"||op=="named_unbundle_select") {classField="unbundler_id";genericClass="NamedUnbundler";}
                if(classField!=null) {
                    LabView.Invoke(helpers["Native_GetObjectInfo"],"SetControlValue",BindingFlags.InvokeMethod,"class_name","");
                    var classInfo=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",input[classField]}},new[]{"class_name"});
                    string actualClass=Convert.ToString(classInfo["class_name"]);
                    checkedClass=actualClass;
                    string expectedClass=genericClass??(op=="set_number"||op=="set_double"||op=="get_number"?"DigitalNumericConstant":op=="set_string"||op=="get_string"?"StringConstant":op=="set_index_mode"?"LoopTunnel":"Digital");
                    bool directionOrDefault=op=="set_indicator"||op=="is_indicator"||op=="control_default";
                    string[] supported=op!="control_default"?new[]{"Digital","Slide","Boolean","String","Cluster","Array"}:new[]{"Digital","Slide","Boolean","String","Cluster"};
                    bool numericRepresentation=op=="representation"||op=="representation_info";
                    if(directionOrDefault?!supported.Contains(actualClass):numericRepresentation?!new[]{"Digital","Slide"}.Contains(actualClass):actualClass!=expectedClass)throw new ArgumentException(op+" received unsupported actual class "+actualClass+" (expected "+(directionOrDefault?"supported control class":numericRepresentation?"Digital or Slide":expectedClass)+"). No typed setter was invoked.");
                }
                Dictionary<string,object> output,verification=null;
                if(op=="file_place") {
                    string source=ViImport.CanonicalLocalPath(Convert.ToString(input["source_path"]));requestedFiles.Add(source);
                    output=NiRecipeFiles.Place(app,target,input,refs,loadedHelperPaths);
                }
                else if(op=="ctl_constant"||op=="global_node") {
                    string path=Convert.ToString(input["type_path"]);bool global=op=="global_node";string field=global?Convert.ToString(input["control_name"]):null;
                    var dependency=RememberTypeDependency(path,generatedDependencies,typeDependencies);ValidateTypePlacement(dependency,global,field);requestedTypes.Add(path);
                    output=TypedFilePlacement.Place(app,target,dependency,Convert.ToUInt32(input["owner_diagram_id"]),Convert.ToInt16(input["x"]),Convert.ToInt16(input["y"]),field,global&&(bool)input["write"],refs,loadedHelperPaths);
                }
                else if(op=="control_default")output=ApplyControlDefault(helpers,vi,target,Convert.ToUInt32(input["control_id"]),checkedClass,input["value"],clusterMembers,controlClasses);
                else if(op=="array_default")output=ApplyArrayDefault(helpers,vi,target,Convert.ToUInt32(input["array_id"]),input["value"]);
                else if(op=="array_move_element")output=MoveArrayElement(helpers,target,Convert.ToUInt32(input["control_id"]),Convert.ToUInt32(input["owner_id"]),Convert.ToInt16(input["x"]),Convert.ToInt16(input["y"]),arrayElements);
                else if(op=="boolean_text"||op=="boolean_text_info")output=ApplyBooleanText(helpers,vi,target,Convert.ToUInt32(input["boolean_id"]),op=="boolean_text"?Convert.ToString(input["text"]):null);
                else if(op=="node_label"||op=="node_label_info")output=ApplyNodeLabel(helpers,target,Convert.ToUInt32(input["node_id"]),op=="node_label"?Convert.ToString(input["label"]):null);
                else if(op=="for_count_info")output=ReadForCountInfo(helpers,target,Convert.ToUInt32(input["for_id"]));
                else if(op=="case_selector_info")output=ReadCaseSelectorInfo(helpers,target,Convert.ToUInt32(input["case_id"]));
                else if(op=="object_info")output=ReadObjectInfo(helpers,target,Convert.ToUInt32(input["object_id"]));
                else if(op=="decoration") {
                    if(Convert.ToUInt32(input["style"])!=3602)throw new ArgumentException("Root Decoration supports Flat Box 3602 only.");
                    var create=new Dictionary<string,object>(input);create["object_id type"]=(uint)0;
                    var native=InvokeHelper(helpers["Own20261012_PublicAddRootDecoration_v2"],create,new[]{"object_id"});uint objectId=Convert.ToUInt32(native["object_id"]);if(objectId==0)throw new InvalidOperationException("Decoration factory returned a null object.");
                    output=ReadObjectInfo(helpers,target,objectId);if(Convert.ToString(output["class_name"])!="Decoration")throw new InvalidOperationException("Factory returned a different actual object class.");
                    uint rootId=Convert.ToUInt32(values["root"]["diagram_id"]);var objects=InvokeHelper(helpers["Native_GetDiagramObjects"],new Dictionary<string,object>{{"diagram_id",rootId}},new[]{"object_ids"});
                    if(!((IEnumerable)objects["object_ids"]).Cast<object>().Any(x=>SameObjectIdentity(helpers,target,objectId,Convert.ToUInt32(x))))throw new InvalidOperationException("Created Decoration is absent from the actual root Diagram.");
                    output["object_id"]=objectId;
                }
                else if(op=="scripting_add") {
                    foreach(string field in new[]{"source_x","source_y"}) {
                        var info=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",input[field]}},new[]{"class_name"});
                        if(!Convert.ToString(info["class_name"]).EndsWith("Terminal",StringComparison.Ordinal))throw new ArgumentException("scripting_add requires actual source Terminals.");
                        var context=InvokeHelper(helpers["Own20261012_GetTerminalContext"],new Dictionary<string,object>{{"terminal_id",input[field]}},new[]{"diagram_id","is_source"});
                        if(!Convert.ToBoolean(context["is_source"])||Convert.ToUInt32(context["diagram_id"])!=Convert.ToUInt32(input["owner_diagram_id"]))throw new ArgumentException("scripting_add accepts sources in the same owned Diagram only.");
                    }
                    output=InvokeHelper(helpers[Data.Text(contract,"helper")],input,new[]{"terminal_id"});
                }
                else if(op=="representation") {
                    uint requested=ValidateNumericRepresentation(input["representation"]);
                    InvokeHelper(helpers["Native_SetNumericRepresentation"],input,new string[0]);
                    verification=ReadNumericRepresentation(helpers,target,Convert.ToUInt32(input["numeric_id"]),clusterMembers);
                    if(Convert.ToUInt32(verification["representation"])!=requested)throw new InvalidOperationException("Numeric representation readback differs from requested value.");
                    verification.Remove("terminal_id");output=new Dictionary<string,object>();
                }
                else if(op=="representation_info")output=ReadNumericRepresentation(helpers,target,Convert.ToUInt32(input["numeric_id"]),clusterMembers);
                else if(op=="connector_auto")output=ApplyConnectorAuto(helpers,target,(string[])input["control_labels"],manifest);
                else if(op=="connector_info")output=ReadConnectorInfo(helpers,target);
                else if(op=="connect_source_to_structure")output=ConnectSourceToStructure(helpers,target,Convert.ToUInt32(input["structure_id"]),Convert.ToUInt32(input["source_terminal"]),structureSourceRequests);
                else if(op=="set_double") {
                    var terminal=InvokeHelper(helpers["Native_GetConstantTerminal"],new Dictionary<string,object>{{"constant_id",input["constant_id"]}},new[]{"terminal_id"});
                    uint terminalId=Convert.ToUInt32(terminal["terminal_id"]);if(terminalId==0)throw new ArgumentException("A DBL constant must expose a current Terminal.");
                    var type=InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",terminalId}},new[]{"type_string"});
                    int[] descriptor=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();
                    double requested=ValidateDoubleConstant(input["value"],descriptor);
                    InvokeHelper(helpers[Data.Text(contract,"helper")],input,new string[0]);
                    output=InvokeHelper(helpers["Own20261012_GetDigitalConstantValue"],new Dictionary<string,object>{{"constant_id",input["constant_id"]}},new[]{"value"});
                    if(!(output["value"] is double)||Convert.ToDouble(output["value"])!=requested)throw new InvalidOperationException("DBL constant readback differs from the requested value.");
                    output["type_descriptor"]=descriptor;
                }
                else if(op=="array_dimensions"||op=="for_stop_if_true"||op=="while_stop_if_true"||op=="tunnel_conditional") {
                    string field=op=="array_dimensions"?"array_id":op=="for_stop_if_true"?"for_id":op=="while_stop_if_true"?"while_id":"tunnel_id";
                    string valueField=op=="array_dimensions"?"dimensions":op=="tunnel_conditional"?"is_conditional":"stop_if_true";
                    string getter=op=="array_dimensions"?"Own20261012_GetArrayInfo":op=="for_stop_if_true"?"Own20261012_GetForStopIfTrue":op=="while_stop_if_true"?"Own20261012_GetWhileStopIfTrue":"Own20261012_GetLoopTunnelConditional";
                    InvokeHelper(helpers[Data.Text(contract,"helper")],input,new string[0]);
                    output=InvokeHelper(helpers[getter],new Dictionary<string,object>{{field,input[field]}},new[]{valueField});
                    if(!ExactControlValueEqual(input[valueField],output[valueField]))throw new InvalidOperationException(op+" readback differs from the requested value.");
                }
                else if(op=="tunnel_condition_terminal") {
                    var enabled=InvokeHelper(helpers["Own20261012_GetLoopTunnelConditional"],input,new[]{"is_conditional"});if(!(enabled["is_conditional"] is bool)||!(bool)enabled["is_conditional"])throw new ArgumentException("Enable a conditional output tunnel before requesting its condition Terminal.");
                    output=InvokeHelper(helpers[Data.Text(contract,"helper")],input,new[]{"condition_terminal_id"});
                    uint terminalId=Convert.ToUInt32(output["condition_terminal_id"]);if(terminalId==0)throw new InvalidOperationException("Conditional tunnel returned an unavailable null Terminal.");
                    var type=InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",terminalId}},new[]{"type_string"});
                    int[] descriptor=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();if(descriptor.Length<2||(descriptor[1]&255)!=0x21)throw new InvalidOperationException("Conditional tunnel Terminal is not Boolean.");
                }
                else if(op=="named_bundle_select"||op=="named_unbundle_select") {
                    bool bundle=op=="named_bundle_select";string field=bundle?"bundler_id":"unbundler_id";
                    object getter=helpers[bundle?"Own20261012_GetNamedBundlerElements":"Own20261012_GetNamedUnbundlerElements"];
                    var selectionInput=new Dictionary<string,object>{{field,input[field]}};
                    string[] metadataFields={"element_names","available_elements","full_element_names"};
                    var before=InvokeHelper(getter,selectionInput,metadataFields);
                    string[] requested=(string[])input["element_names"];
                    ValidateNamedSelection(requested,TextArray(before["element_names"]),TextArray(before["available_elements"]));
                    InvokeHelper(helpers[Data.Text(contract,"helper")],input,new string[0]);
                    output=InvokeHelper(getter,selectionInput,metadataFields);
                    VerifyNamedSelectionReadback(requested,TextArray(output["element_names"]));
                } else output=op=="find_control"?FindControl(helpers,target,Convert.ToString(input["label"])):InvokeHelper(helpers[Data.Text(contract,"helper")],input,Data.Array(contract["outputs"]).Select(Convert.ToString).ToArray());
                if(op=="set_string") {
                    var read=InvokeHelper(helpers["Native_GetStringConstantValue"],new Dictionary<string,object>{{"constant_id",input["constant_id"]}},new[]{"value"});
                    if(!(read["value"] is string)||!String.Equals((string)input["value"],(string)read["value"],StringComparison.Ordinal))throw new InvalidOperationException("String constant readback differs from requested text.");
                }
                if(op=="get_string"&&!(output["value"] is string))throw new InvalidOperationException("String constant getter did not return text.");
                if(op=="set_indicator") {var read=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",input["control_id"]}},new[]{"is_indicator"});if(Convert.ToBoolean(read["is_indicator"])!=(bool)input["is_indicator"])throw new InvalidOperationException("Control direction readback differs from the requested value.");}
                if(op=="get_number"&&!IsFiniteNumeric(output["value"]))throw new InvalidOperationException("Numeric constant getter did not return a finite number.");
                if(op=="control"&&Convert.ToUInt32(input["style"])==3400) {string key=ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(output["object_id"]));clusterMembers[key]=new List<string>();controlClasses[key]="Cluster";}
                if(op=="control"&&Convert.ToUInt32(input["style"])==3300)arrayElements[ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(output["object_id"]))]=null;
                if(op=="array_element") {
                    uint ownerId=Convert.ToUInt32(input["owner_id"]),childId=Convert.ToUInt32(output["object_id"]);
                    var info=ReadArrayInfo(helpers,ownerId);if(childId==0||!SameObjectIdentity(helpers,target,childId,Convert.ToUInt32(info["element_id"])))throw new InvalidOperationException("Array element does not match the newly created child object identity.");
                    var childClass=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",childId}},new[]{"class_name"});string expected=Convert.ToUInt32(input["style"])==3000?"Digital":Convert.ToUInt32(input["style"])==3200?"String":"Boolean";if(Convert.ToString(childClass["class_name"])!=expected)throw new InvalidOperationException("Array element class differs from requested style.");arrayElements[ReadObjectIdentityKey(helpers,target,ownerId)]=ReadObjectIdentityKey(helpers,target,childId);
                }
                if(op=="array_info"&&(Convert.ToUInt32(output["element_id"])==0||Convert.ToUInt32(output["terminal_id"])==0))throw new InvalidOperationException("Array info returned an unavailable element or Terminal.");
                if(op=="cluster_child") {
                    uint style=Convert.ToUInt32(input["style"]);
                    string expected=style==3000?"Digital":style==3110?"Boolean":style==3200?"String":style==3400?"Cluster":null;
                    if(expected==null)throw new ArgumentException("Unsupported Cluster child style");
                    var childClass=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",output["object_id"]}},new[]{"class_name"});
                    if(Convert.ToString(childClass["class_name"])!=expected)throw new InvalidOperationException("Cluster child class differs from requested style: "+childClass["class_name"]);
                    string childKey=ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(output["object_id"])),ownerKey=ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(input["owner_id"]));controlClasses[childKey]=expected;
                    if(args.ContainsKey("representation")) {
                        uint representation=ValidateClusterChildRepresentation(Data.Text(args,"style"),args["representation"]);
                        InvokeHelper(helpers["Native_SetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",output["object_id"]},{"representation",representation}},new string[0]);
                        var actual=InvokeHelper(helpers["Own20261012_GetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",output["object_id"]}},new[]{"representation"});
                        if(ValidateNumericRepresentation(actual["representation"])!=representation)throw new InvalidOperationException("Nested Digital representation readback differs.");
                        verification=new Dictionary<string,object>{{"representation",representation},{"representation_name","I32"},{"child_identity",childKey},{"independent_diagram_terminal",false}};
                    }
                    if(clusterMembers.ContainsKey(ownerKey))clusterMembers[ownerKey].Add(childKey);
                    if(expected=="Cluster")clusterMembers[childKey]=new List<string>();
                }
                if(deletedIdentity!=null)RemoveContainerIdentity(deletedIdentity,clusterMembers,controlClasses,arrayElements);
                if(output.ContainsKey("terminal_ids")) {
                    var labels=new List<string>();var terminals=new List<uint>();
                    foreach(object t in (IEnumerable)output["terminal_ids"]) {
                        var named=InvokeHelper(helpers["Native_GetTerminalName"],new Dictionary<string,object>{{"terminal_id",Convert.ToUInt32(t)}},new[]{"terminal_name"});
                        labels.Add(Convert.ToString(named["terminal_name"]));terminals.Add(Convert.ToUInt32(t));
                    }
                    output["pins"]=PinAliases(labels,terminals);
                    output["terminal_names"]=labels.ToArray();
                    output["ambiguous_pin_names"]=labels.Where(s=>!String.IsNullOrEmpty(s)).GroupBy(s=>s,StringComparer.Ordinal).Where(g=>g.Count()>1).Select(g=>g.Key).ToArray();
                }
                if(op=="control_default"||op=="array_default") {
                    uint defaultId=Convert.ToUInt32(input[op=="control_default"?"control_id":"array_id"]);
                    object defaultValue=op=="array_default"?LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,Convert.ToString(output["label"])):output["value"];
                    pendingDefaults[ReadObjectIdentityKey(helpers,target,defaultId)]=defaultValue;
                }
                values[id]=output;log.Add(new{id,operation=op,outputs=output,verification});
            }
            // Helper VIs used for scripting can recompile the target and restore
            // its current values. Commit all surviving top-level defaults only
            // after the last graph edit, with no helper Run between Set calls.
            LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,target,false,false);
            // The first save materializes the final compiled control layout;
            // defaults on newly terminal-derived controls can otherwise remain
            // the type artifact's old values despite successful current reads.
            CommitFinalDefaults(helpers,vi,target,pendingDefaults);
            if(pendingDefaults.Count>0)LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,target,false,false);
            var diagrams=new HashSet<uint>();diagrams.Add(Convert.ToUInt32(values["root"]["diagram_id"]));
            var auditScope=RecipeReferenceGuard.DiagramAuditScope(doc,catalog);
            foreach(string reference in RecipeReferenceGuard.LiveDiagramReferences(doc,catalog)) {
                string[] part=reference.Substring(1).Split('.');object value=values[part[0]][part[1]];
                if(value is IEnumerable && !(value is string))foreach(object d in (IEnumerable)value)diagrams.Add(Convert.ToUInt32(d));
                else diagrams.Add(Convert.ToUInt32(value));
            }
            var broken=new List<uint>();
            foreach(uint diagram in diagrams) {
                var wires=InvokeHelper(helpers["Native_GetDiagramWires"],new Dictionary<string,object>{{"diagram_id",diagram}},new[]{"wire_ids"});
                foreach(object w in (IEnumerable)wires["wire_ids"]) {
                    var status=InvokeHelper(helpers["Native_GetWireBroken"],new Dictionary<string,object>{{"wire_id",Convert.ToUInt32(w)}},new[]{"is_broken"});
                    if(Convert.ToBoolean(status["is_broken"]))broken.Add(Convert.ToUInt32(w));
                }
            }
            string html=Path.Combine(folder,"documentation");Directory.CreateDirectory(html);
            LabView.Invoke(vi,"PrintVIToHTML",BindingFlags.InvokeMethod,Path.Combine(html,"VI.html"),false,4,0,24,html);
            object state=LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty);
            var savedCallees=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var savedClosure=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(object callee in (IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)) {
                string savedPath=Convert.ToString(callee);if(String.IsNullOrWhiteSpace(savedPath))continue;
                savedPath=TypeImport.NativePath(savedPath);savedCallees.Add(savedPath);
                if(requestedFiles.Count!=0&&(TypeImport.IsNativeDependency(savedPath)||TypeImport.IsPreparedDependency(savedPath)))savedClosure.Add(savedPath);
                else AuditGeneratedDependency(app,savedPath,refs,generatedDependencies,typeDependencies,savedClosure);
            }
            if(requestedFiles.Count!=0)Storage.WriteNew(Path.Combine(folder,"file-dependency-report.json"),NiRecipeFiles.AuditSaved(app,target,requestedFiles,savedCallees.Where(p=>TypeImport.IsNativeDependency(p)||TypeImport.IsPreparedDependency(p)),refs,loadedHelperPaths));
            foreach(var dependency in generatedDependencies.Values)dependency.VerifyUnchanged();
            foreach(var dependency in typeDependencies.Values)dependency.VerifyUnchanged();
            if(copySource!=null) {
                copySource.VerifyUnchanged();
                Storage.WriteNew(Path.Combine(folder,"source-copy-report.json"),new{source=copySource.ToEvidence(),target_path=target,source_file_unchanged=true,source_saved=false,source_executed=false,target_executed=false,scope="Disk copy of a previously generated application VI; edits affect the unique copy only"});
            }
            // A later delete may intentionally remove a placed subVI or its
            // containing structure. Audit what was saved and report the delta.
            Storage.WriteNew(Path.Combine(folder,"subvi-dependency-report.json"),new{checked_sources=generatedDependencies.Values.Select(d=>new{path=d.Path,sha256=d.Sha256,generation_report=d.ReportPath}).ToArray(),source_files_unchanged=true,native_callee_paths_checked=true,saved_direct_callees=savedCallees.OrderBy(x=>x).ToArray(),saved_dependency_closure=savedClosure.OrderBy(x=>x).ToArray(),requested_subvis_absent_after_edits=requestedSubvis.Except(savedCallees,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray(),scope="Actual loaded dependency paths and disk provenance after saving target; in-memory unsaved edits are not independently checked",dependencies_executed=false});
            Storage.WriteNew(Path.Combine(folder,"type-dependency-report.json"),new{checked_types=typeDependencies.Values.Select(d=>new{path=d.Path,sha256=d.Hash,report_path=d.ReportPath,report_sha256=d.ReportHash,artifact_kind=d.Kind,vi_type=d.VIType}).ToArray(),source_files_unchanged=true,source_reports_unchanged=true,native_type_paths_and_callees_checked=true,saved_direct_callees=savedCallees.OrderBy(x=>x).ToArray(),saved_dependency_closure=savedClosure.OrderBy(x=>x).ToArray(),requested_types_absent_from_saved_callees=requestedTypes.Except(savedCallees,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray(),scope="Actual loaded type identity, VIType and empty native callees; disk artifact/report hashes preserved. A regular control may embed without a saved type dependency; deleted placements may also disappear. Unsaved in-memory edits are not independently checked.",dependencies_executed=false});
            var report=new{created=true,vi_path=target,vi_sha256=Data.Hash(target),labview_version=LabView.Invoke(app,"Version",BindingFlags.GetProperty),execution_state=state,static_checks_passed=broken.Count==0&&Convert.ToInt32(state)==1,generated_vi_executed=false,step_count=steps.Length,steps=log,broken_wire_ids=broken,inspected_diagram_count=diagrams.Count,diagram_audit_scope=auditScope,helper_callees=dependencies.OrderBy(s=>s).ToArray(),helper_names=needed.OrderBy(s=>s).ToArray(),loaded_helper_paths=loadedHelperPaths,complete_replacement=false,limitations=new[]{"Static compilation and live known-diagram checks only; audit scope excludes invalidated references and does not prove recursive coverage","Generic recipes are never run by the builder","Operations backed by audited NI helpers; authoring provenance retained per helper","Property/Invoke metadata is a snapshot; reacquire after class, selection or reference wiring changes","Formula authoring is supported; finite representative runtime cases are accepted separately","Defaults cover finite scalar values, known current-recipe Cluster member trees and typed rank 1/2 DBL, Boolean or String Arrays; unknown Cluster layouts and other complex element types remain unsupported; native acceptance evidence is recorded separately"}};
            Storage.WriteNew(Path.Combine(folder,"recipe-report.json"),report);return report;
        } catch(Exception ex) {string failure=Path.Combine(folder,"recipe-failure.json");if(!File.Exists(failure))Storage.WriteNew(failure,new{created=false,error=Data.Error(ex),completed_steps=log,partial_path=target});throw;}
        finally{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);LabView.Release(app);lease.Dispose();}
    }
    public static string SelectAddedTunnel(string[] before,string[] after) {
        if(before==null||after==null||before.Length>1000||after.Length>1000||before.Concat(after).Any(String.IsNullOrWhiteSpace))throw new ArgumentException("Bounded nonempty tunnel identities required.");
        var old=new HashSet<string>(before,StringComparer.OrdinalIgnoreCase);var current=new HashSet<string>(after,StringComparer.OrdinalIgnoreCase);
        if(old.Count!=before.Length||current.Count!=after.Length)throw new InvalidOperationException("Duplicate physical tunnel identity.");
        if(!old.IsSubsetOf(current))throw new InvalidOperationException("Connection replaced or removed an existing tunnel; isolated generation rejected.");
        var added=current.Except(old,StringComparer.OrdinalIgnoreCase).ToArray();if(added.Length!=1)throw new InvalidOperationException("Connection must add exactly one distinct tunnel.");return added[0];
    }
    static uint[] NativeRefs(object value){var list=value as IEnumerable;if(list==null||value is string)throw new InvalidOperationException("Native reference array required.");var refs=list.Cast<object>().Select(Convert.ToUInt32).ToArray();if(refs.Length>1000||refs.Any(x=>x==0))throw new InvalidOperationException("Unavailable or oversized native reference array.");return refs;}
    static Dictionary<string,uint> TunnelIdentities(Dictionary<string,object> helpers,string target,object refs){var result=new Dictionary<string,uint>(StringComparer.OrdinalIgnoreCase);foreach(uint id in NativeRefs(refs)){string identity=ReadObjectIdentityKey(helpers,target,id);if(result.ContainsKey(identity))throw new InvalidOperationException("Duplicate physical tunnel in native enumeration.");result[identity]=id;}return result;}
    static Dictionary<string,object> ConnectSourceToStructure(Dictionary<string,object> helpers,string target,uint structure,uint source,HashSet<string> requests) {
        var info=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",structure}},new[]{"class_name"});string kind=Convert.ToString(info["class_name"]);if(!new[]{"ForLoop","WhileLoop","CaseStructure","EventStructure"}.Contains(kind))throw new ArgumentException("Source connection requires an accepted For/While/Case/Event structure.");
        string structureIdentity=ReadObjectIdentityKey(helpers,target,structure),sourceIdentity=ReadObjectIdentityKey(helpers,target,source);
        var context=InvokeHelper(helpers["Own20261012_GetTerminalContext"],new Dictionary<string,object>{{"terminal_id",source}},new[]{"is_source","diagram_id"});if(!(context["is_source"] is bool)||!(bool)context["is_source"])throw new ArgumentException("Connection requires a source Terminal, not a sink.");
        var args=new Dictionary<string,object>{{"structure_id",structure}};var before=InvokeHelper(helpers["Own20261012_GetStructureInfo"],args,new[]{"owner_id","diagram_ids","tunnel_ids"});
        if(!SameObjectIdentity(helpers,target,Convert.ToUInt32(context["diagram_id"]),Convert.ToUInt32(before["owner_id"])))throw new ArgumentException("Source must belong to the structure's direct parent diagram.");
        string pair=structureIdentity+"\n"+sourceIdentity;if(!requests.Add(pair))throw new ArgumentException("Repeated source-to-structure connection would replace a native tunnel; rejected before mutation.");
        var old=TunnelIdentities(helpers,target,before["tunnel_ids"]);
        InvokeHelper(helpers["Own20261012_ConnectSourceToStructure"],new Dictionary<string,object>{{"structure_id",structure},{"source_terminal",source}},new string[0]);
        var after=InvokeHelper(helpers["Own20261012_GetStructureInfo"],args,new[]{"owner_id","diagram_ids","tunnel_ids"});
        if(!SameObjectIdentity(helpers,target,Convert.ToUInt32(before["owner_id"]),Convert.ToUInt32(after["owner_id"])))throw new InvalidOperationException("Structure owner changed during connection.");
        var current=TunnelIdentities(helpers,target,after["tunnel_ids"]);string added=SelectAddedTunnel(old.Keys.ToArray(),current.Keys.ToArray());uint tunnel=current[added];
        var terms=InvokeHelper(helpers["Native_GetTunnelTerminals_v2"],new Dictionary<string,object>{{"tunnel_id",tunnel}},new[]{"inside_ids","outside_id"});uint[] inside=NativeRefs(terms["inside_ids"]),diagrams=NativeRefs(after["diagram_ids"]);uint outside=Convert.ToUInt32(terms["outside_id"]);
        if(outside==0||inside.Length==0||inside.Length!=diagrams.Length)throw new InvalidOperationException("New tunnel terminal coverage differs from current structure diagrams.");
        foreach(uint terminal in inside.Concat(new[]{outside}))ReadObjectIdentityKey(helpers,target,terminal);
        return new Dictionary<string,object>{{"tunnel_id",tunnel},{"inside_ids",inside},{"outside_id",outside},{"structure_class",kind},{"tunnel_identity",added}};
    }
    public static void ValidateConnectorLabels(string[] labels) {
        if(labels==null||labels.Length<1||labels.Length>28||labels.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>4096)||labels.Distinct(StringComparer.Ordinal).Count()!=labels.Length)throw new ArgumentException("Connector allocation requires 1..28 distinct nonempty top-level control labels.");
    }
    public static Dictionary<string,object> ChooseConnectorLayout(object[] controls,object[] patterns) {
        if(controls==null||patterns==null||patterns.Length>36)throw new ArgumentException("Bounded connector controls and geometry required.");
        var rows=controls.Select(Data.Map).ToArray();ValidateConnectorLabels(rows.Select(x=>Data.Text(x,"label")).ToArray());
        var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)if(!(row["is_indicator"] is bool)||!identities.Add(Data.Text(row,"identity")))throw new ArgumentException("Actual Boolean direction and distinct physical controls are required.");
        var inputs=rows.Where(x=>!(bool)x["is_indicator"]).ToArray();var outputs=rows.Where(x=>(bool)x["is_indicator"]).ToArray();
        var candidates=new List<Dictionary<string,object>>();var patternIds=new HashSet<uint>();
        foreach(var raw in patterns) {
            var p=Data.Map(raw);if(!p.ContainsKey("ni_geometry_verified")||!(p["ni_geometry_verified"] is bool)||!(bool)p["ni_geometry_verified"])continue;
            uint pattern=ValidateConnectorPattern(p["pattern"]);if(!patternIds.Add(pattern))throw new ArgumentException("Duplicate connector pattern.");
            var slots=Data.Array(p["slots"]).Select(Data.Map).ToArray();if(slots.Length<1||slots.Length>28)throw new ArgumentException("Invalid connector slot count.");
            var seen=new HashSet<int>();var geometry=new List<ConnectorSlot>();
            foreach(var s in slots){double index=Data.Number(s["index"]);if(index!=Math.Truncate(index)||index<0||index>=slots.Length||!seen.Add((int)index))throw new ArgumentException("Invalid connector slot index.");int[] b=ConnectorBounds(Data.Array(s["rect"]));geometry.Add(new ConnectorSlot{Index=(int)index,Bounds=b,Cx=b[0]+b[2],Cy=b[1]+b[3]});}
            int middle=geometry.Min(x=>x.Bounds[0])+geometry.Max(x=>x.Bounds[2]);
            var left=geometry.Where(x=>x.Cx<middle).OrderBy(x=>x.Cx).ThenBy(x=>x.Cy).ThenBy(x=>x.Index).ToArray();var right=geometry.Where(x=>x.Cx>middle).OrderByDescending(x=>x.Cx).ThenBy(x=>x.Cy).ThenBy(x=>x.Index).ToArray();
            if(inputs.Length>left.Length||outputs.Length>right.Length)continue;
            var assign=new List<object>();
            for(int side=0;side<2;side++){var cc=side==0?inputs:outputs;var ss=side==0?left:right;for(int i=0;i<cc.Length;i++)assign.Add(new Dictionary<string,object>{{"label",cc[i]["label"]},{"identity",cc[i]["identity"]},{"is_indicator",side==1},{"terminal_index",ss[i].Index},{"side",side==0?"left":"right"}});}
            candidates.Add(new Dictionary<string,object>{{"pattern",pattern},{"terminal_count",slots.Length},{"terminal_bounds",geometry.OrderBy(x=>x.Index).Select(x=>(object)x.Bounds).ToArray()},{"assignments",assign.ToArray()}});
        }
        if(candidates.Count==0)throw new ArgumentException("No verified connector pattern has enough left/right terminals; maximum 14 per side.");
        return candidates.OrderBy(x=>Convert.ToInt32(x["terminal_count"])).ThenBy(x=>Convert.ToUInt32(x["pattern"])).First();
    }
    sealed class ConnectorSlot {public int Index,Cx,Cy;public int[] Bounds;}
    static uint ValidateConnectorPattern(object value){if(!IsFiniteNumeric(value))throw new ArgumentException("Connector pattern must be numeric.");double n=Convert.ToDouble(value);if(n!=Math.Truncate(n)||n<4800||n>4835)throw new ArgumentException("Connector pattern must be an integer 4800..4835.");return (uint)n;}
    static int[] ConnectorBounds(object[] raw){if(raw.Length!=4)throw new ArgumentException("Connector bounds require four coordinates.");var b=new int[4];for(int i=0;i<4;i++){if(!IsFiniteNumeric(raw[i]))throw new ArgumentException("Connector bounds must be numeric.");double n=Convert.ToDouble(raw[i]);if(n!=Math.Truncate(n)||n<0||n>32)throw new ArgumentException("Connector coordinates must be integral 0..32.");b[i]=(int)n;}if(b[2]<=b[0]||b[3]<=b[1])throw new ArgumentException("Empty connector rectangle.");return b;}
    static Dictionary<string,object> ReadConnectorInfo(Dictionary<string,object> helpers,string target) {
        var read=InvokeHelper(helpers["Own20261012_GetConnectorInfo"],new Dictionary<string,object>{{"target_path",target}},new[]{"pattern","terminal_count","terminal_bounds","control_ids"});
        uint pattern=ValidateConnectorPattern(read["pattern"]);int count=Convert.ToInt32(read["terminal_count"]);var refs=((IEnumerable)read["control_ids"]).Cast<object>().Select(Convert.ToUInt32).ToArray();var bounds=((IEnumerable)read["terminal_bounds"]).Cast<object>().Select(x=>ConnectorBounds(((IEnumerable)x).Cast<object>().ToArray())).ToArray();
        if(count<1||count>28||refs.Length!=count||bounds.Length!=count)throw new InvalidOperationException("Native connector metadata lengths differ.");
        var assignments=new List<object>();
        for(int i=0;i<count;i++)if(refs[i]!=0){string identity=ReadObjectIdentityKey(helpers,target,refs[i]);var label=InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",refs[i]}},new[]{"label"});var direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",refs[i]}},new[]{"is_indicator"});if(!(direction["is_indicator"] is bool))throw new InvalidOperationException("Native connector direction is not Boolean.");assignments.Add(new Dictionary<string,object>{{"terminal_index",i},{"label",Convert.ToString(label["label"])},{"identity",identity},{"is_indicator",direction["is_indicator"]}});}
        return new Dictionary<string,object>{{"pattern",pattern},{"terminal_count",count},{"terminal_bounds",bounds.Select(x=>(object)x).ToArray()},{"assignments",assignments.ToArray()}};
    }
    static Dictionary<string,object> ApplyConnectorAuto(Dictionary<string,object> helpers,string target,string[] labels,Dictionary<string,object> manifest) {
        ValidateConnectorLabels(labels);var controls=new List<object>();var refs=new Dictionary<string,uint>(StringComparer.Ordinal);
        foreach(string label in labels){var found=FindControl(helpers,target,label);uint id=Convert.ToUInt32(found["control_id"]);string identity=ReadObjectIdentityKey(helpers,target,id);var direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",id}},new[]{"is_indicator"});controls.Add(new Dictionary<string,object>{{"label",label},{"identity",identity},{"is_indicator",direction["is_indicator"]}});refs[label]=id;}
        string geometryPath=Path.Combine(Storage.Root,"recipes","connector-patterns.json");Storage.NoReparse(geometryPath);if(Data.Hash(geometryPath)!=Data.Text(manifest,"connector_patterns_sha256"))throw new IOException("Unverified connector geometry catalog.");var geometry=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(geometryPath,Encoding.UTF8)));
        var layout=ChooseConnectorLayout(controls.ToArray(),Data.Array(geometry["patterns"]));
        var before=ReadConnectorInfo(helpers,target);
        if(Data.Array(before["assignments"]).Length!=0) {
            InvokeHelper(helpers["Own20261012_PublicDisconnectConnectorAll_v1"],new Dictionary<string,object>{{"target_path",target}},new string[0]);
            var cleared=ReadConnectorInfo(helpers,target);
            if(Convert.ToUInt32(cleared["pattern"])!=Convert.ToUInt32(before["pattern"])||Convert.ToInt32(cleared["terminal_count"])!=Convert.ToInt32(before["terminal_count"])||Data.Array(cleared["assignments"]).Length!=0)throw new InvalidOperationException("Connector disconnection readback differs.");
        }
        InvokeHelper(helpers["Own20261012_SetConnectorPattern"],new Dictionary<string,object>{{"target_path",target},{"pattern",layout["pattern"]}},new string[0]);
        foreach(var a in Data.Array(layout["assignments"]).Select(Data.Map))InvokeHelper(helpers["Native_AssignConnectorTerminal"],new Dictionary<string,object>{{"target_path",target},{"control_id",refs[Data.Text(a,"label")]},{"terminal_index",checked((short)Convert.ToInt32(a["terminal_index"]))}},new string[0]);
        var actual=ReadConnectorInfo(helpers,target);VerifyConnectorReadback(layout,actual);return actual;
    }
    public static void VerifyConnectorReadback(Dictionary<string,object> expected,Dictionary<string,object> actual) {
        if(Convert.ToUInt32(expected["pattern"])!=Convert.ToUInt32(actual["pattern"])||Convert.ToInt32(expected["terminal_count"])!=Convert.ToInt32(actual["terminal_count"]))throw new InvalidOperationException("Connector pattern/count readback differs.");
        var eb=Data.Array(expected["terminal_bounds"]);var ab=Data.Array(actual["terminal_bounds"]);if(eb.Length!=ab.Length)throw new InvalidOperationException("Connector geometry size differs.");for(int i=0;i<eb.Length;i++)if(!((IEnumerable)eb[i]).Cast<object>().Select(Convert.ToInt32).SequenceEqual(((IEnumerable)ab[i]).Cast<object>().Select(Convert.ToInt32)))throw new InvalidOperationException("Connector geometry readback differs.");
        var ea=Data.Array(expected["assignments"]).Select(Data.Map).ToArray();var aa=Data.Array(actual["assignments"]).Select(Data.Map).ToArray();if(ea.Length!=aa.Length)throw new InvalidOperationException("Unexpected connector assignments.");
        foreach(var e in ea){var matches=aa.Where(a=>Convert.ToInt32(a["terminal_index"])==Convert.ToInt32(e["terminal_index"])).ToArray();if(matches.Length!=1||!String.Equals(Data.Text(matches[0],"identity"),Data.Text(e,"identity"),StringComparison.OrdinalIgnoreCase)||!String.Equals(Data.Text(matches[0],"label"),Data.Text(e,"label"),StringComparison.Ordinal)||!(matches[0]["is_indicator"] is bool)||(bool)matches[0]["is_indicator"]!=(bool)e["is_indicator"])throw new InvalidOperationException("Connector slot identity/direction readback differs.");}
    }
    public static uint ValidateNumericRepresentation(object value) {
        if(!IsFiniteNumeric(value))throw new ArgumentException("Numeric representation requires a finite number, not Boolean or text.");
        double n=Convert.ToDouble(value);
        if(n!=Math.Truncate(n)||n<0||n>14)throw new ArgumentException("Numeric representation must be an integer 0..14; native out-of-range clamping is not accepted.");
        return (uint)n;
    }
    public static uint ValidateClusterChildRepresentation(string style,object value) {
        if(style!="Numeric Control"||ValidateNumericRepresentation(value)!=3)throw new ArgumentException("Optional Cluster child representation currently supports Numeric Control I32 (3) only.");
        return 3;
    }
    public static int[] NativeObjectPosition(object raw) {
        var values=raw as IEnumerable;if(values==null||raw is string||raw is IDictionary)throw new InvalidOperationException("Native position must contain two numeric coordinates.");
        var items=values.Cast<object>().ToArray();if(items.Length!=2||items.Any(x=>!IsFiniteNumeric(x)||Convert.ToDouble(x)!=Math.Truncate(Convert.ToDouble(x))||Convert.ToDouble(x)<Int32.MinValue||Convert.ToDouble(x)>Int32.MaxValue))throw new InvalidOperationException("Native position must contain two exact Int32 coordinates.");
        return items.Select(Convert.ToInt32).ToArray();
    }
    static Dictionary<string,object> ReadObjectInfo(Dictionary<string,object> helpers,string target,uint objectId) {
        string identity=ReadObjectIdentityKey(helpers,target,objectId);
        var info=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",objectId}},new[]{"class_name","bounds"});info["bounds"]=NativeObjectBounds(info["bounds"]);
        var position=InvokeHelper(helpers["Own20261012_PublicGetObjectPosition"],new Dictionary<string,object>{{"object_id",objectId}},new[]{"position"});info["position"]=NativeObjectPosition(position["position"]);
        if(identity!=ReadObjectIdentityKey(helpers,target,objectId))throw new InvalidOperationException("Object identity changed during metadata inspection.");
        info["identity"]=identity;return info;
    }
    public static void ValidateNumericDescriptor(uint representation,int[] descriptor) {
        if(representation>14||descriptor==null||descriptor.Length<2||(descriptor[1]&255)!=NumericDescriptorCodes[representation])throw new InvalidOperationException("Fresh Numeric terminal descriptor does not match representation.");
    }
    public static int[] ClusterMemberDescriptor(int[] descriptor,int[] indices,int[] memberCounts) {
        if(descriptor==null||indices==null||memberCounts==null||indices.Length==0||indices.Length!=memberCounts.Length||indices.Length>16)throw new ArgumentException("A bounded nested Cluster member path is required.");
        int[] current=descriptor;
        for(int depth=0;depth<indices.Length;depth++) {
            int index=indices[depth],count=memberCounts[depth];
            if(current.Length<3||(current[1]&255)!=0x50||current[2]!=count||index<0||index>=count)throw new InvalidOperationException("Actual parent Cluster descriptor differs from the known member layout.");
            int cursor=3;int[] selected=null;
            for(int member=0;member<count;member++) {
                if(cursor>=current.Length)throw new InvalidOperationException("Truncated Cluster member descriptor.");
                int bytes=current[cursor]&65535;if(bytes<4||(bytes&1)!=0||cursor+bytes/2>current.Length)throw new InvalidOperationException("Invalid Cluster member descriptor length.");
                if(member==index)selected=current.Skip(cursor).Take(bytes/2).ToArray();cursor+=bytes/2;
            }
            current=selected;
        }
        return current;
    }
    static Dictionary<string,object> ReadNumericRepresentation(Dictionary<string,object> helpers,string target,uint numericId,Dictionary<string,List<string>> clusterMembers) {
        string numericIdentity=ReadObjectIdentityKey(helpers,target,numericId);
        var read=InvokeHelper(helpers["Own20261012_GetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",numericId}},new[]{"representation"});
        uint representation=ValidateNumericRepresentation(read["representation"]);
        uint owner=Convert.ToUInt32(InvokeHelper(helpers["Own20261012_PublicGetControlOwner_v1"],new Dictionary<string,object>{{"control_id",numericId}},new[]{"owner_id"})["owner_id"]);
        if(owner==0)throw new InvalidOperationException("Numeric control returned an unavailable actual owner.");ReadObjectIdentityKey(helpers,target,owner);
        var ownerClass=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",owner}},new[]{"class_name"});
        if(Convert.ToString(ownerClass["class_name"])=="Array") {
            ReadObjectIdentityKey(helpers,target,owner);var array=ReadArrayInfo(helpers,owner);
            if(!SameObjectIdentity(helpers,target,numericId,Convert.ToUInt32(array["element_id"])))throw new InvalidOperationException("Numeric control differs from the actual Array element.");
            uint parentTerminal=Convert.ToUInt32(array["terminal_id"]);int[] parentDescriptor=NativeTerminalDescriptor(helpers,parentTerminal);
            if(parentDescriptor.Length<3||(parentDescriptor[1]&255)!=0x40||parentDescriptor[2]<1||parentDescriptor[2]>2)throw new InvalidOperationException("Nested numeric Array requires an actual rank 1 or 2 descriptor.");
            int cursor=3+2*parentDescriptor[2];if(cursor>=parentDescriptor.Length)throw new InvalidOperationException("Array element descriptor is unavailable.");
            int bytes=parentDescriptor[cursor]&65535;if(bytes<4||(bytes&1)!=0||cursor+bytes/2>parentDescriptor.Length)throw new InvalidOperationException("Invalid numeric Array element descriptor length.");
            int[] elementDescriptor=parentDescriptor.Skip(cursor).Take(bytes/2).ToArray();ValidateNumericDescriptor(representation,elementDescriptor);
            return new Dictionary<string,object>{{"representation",representation},{"representation_name",NumericRepresentationNames[representation]},{"type_descriptor",elementDescriptor},{"owner_terminal_id",parentTerminal},{"owner_type_descriptor",parentDescriptor},{"independent_diagram_terminal",false},{"numeric_identity",numericIdentity},{"owner_identity",ReadObjectIdentityKey(helpers,target,owner)}};
        }
        if(Convert.ToString(ownerClass["class_name"])=="Cluster") {
            var indices=new List<int>();var counts=new List<int>();string childIdentity=numericIdentity;uint top=owner;
            for(int depth=0;;depth++) {
                if(depth>=16)throw new InvalidOperationException("Nested Cluster owner depth exceeds 16.");
                string ownerIdentity=ReadObjectIdentityKey(helpers,target,top);List<string> members;
                if(!clusterMembers.TryGetValue(ownerIdentity,out members))throw new InvalidOperationException("Nested numeric parent requires a known current-recipe Cluster member layout.");
                int index=members.FindIndex(x=>String.Equals(x,childIdentity,StringComparison.OrdinalIgnoreCase));if(index<0)throw new InvalidOperationException("Numeric control is absent from its actual parent Cluster member layout.");
                indices.Insert(0,index);counts.Insert(0,members.Count);
                uint parent=Convert.ToUInt32(InvokeHelper(helpers["Own20261012_PublicGetControlOwner_v1"],new Dictionary<string,object>{{"control_id",top}},new[]{"owner_id"})["owner_id"]);
                string kind=Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",parent}},new[]{"class_name"})["class_name"]);
                if(kind!="Cluster")break;childIdentity=ownerIdentity;top=parent;
            }
            uint parentTerminal=Convert.ToUInt32(InvokeHelper(helpers["Native_GetControlTerminal"],new Dictionary<string,object>{{"control_id",top}},new[]{"terminal_id"})["terminal_id"]);if(parentTerminal==0)throw new InvalidOperationException("Parent Cluster has no available diagram Terminal.");
            int[] parentDescriptor=NativeTerminalDescriptor(helpers,parentTerminal),memberDescriptor=ClusterMemberDescriptor(parentDescriptor,indices.ToArray(),counts.ToArray());ValidateNumericDescriptor(representation,memberDescriptor);
            if(numericIdentity!=ReadObjectIdentityKey(helpers,target,numericId))throw new InvalidOperationException("Nested numeric identity changed during inspection.");
            return new Dictionary<string,object>{{"representation",representation},{"representation_name",NumericRepresentationNames[representation]},{"type_descriptor",memberDescriptor},{"owner_terminal_id",parentTerminal},{"owner_type_descriptor",parentDescriptor},{"member_indices",indices.ToArray()},{"independent_diagram_terminal",false},{"numeric_identity",numericIdentity},{"owner_identity",ReadObjectIdentityKey(helpers,target,top)}};
        }
        var terminal=InvokeHelper(helpers["Native_GetControlTerminal"],new Dictionary<string,object>{{"control_id",numericId}},new[]{"terminal_id"});
        uint terminalId=Convert.ToUInt32(terminal["terminal_id"]);if(terminalId==0)throw new InvalidOperationException("Numeric control returned an unavailable Terminal.");
        var type=InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",terminalId}},new[]{"type_string"});
        int[] descriptor=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();ValidateNumericDescriptor(representation,descriptor);
        return new Dictionary<string,object>{{"representation",representation},{"representation_name",NumericRepresentationNames[representation]},{"terminal_id",terminalId},{"type_descriptor",descriptor},{"independent_diagram_terminal",true}};
    }
    public static double ValidateDoubleConstant(object value,int[] descriptor) {
        double result=Data.Number(value);
        if(descriptor==null||descriptor.Length<2||(descriptor[1]&255)!=0x0A)throw new ArgumentException("set_double requires a fresh scalar DBL terminal descriptor (0x0A); use the integer setter for integer constants.");
        return result;
    }
    // JSON arrays become homogeneous typed rectangular SAFEARRAY values at the COM boundary.
    // Cluster object[] tuples deliberately use a separate conversion path.
    public static Array ToTypedArray(object raw,string elementClass) {
        var rows=raw as object[];if(rows==null||rows.Length>4096)throw new ArgumentException("Array value must be a bounded JSON array.");
        bool matrix=rows.Length>0&&rows[0] is object[];int columns=matrix?((object[])rows[0]).Length:0;
        if(matrix&&(rows.Length>256||columns<1||columns>256||rows.Length*columns>4096))throw new ArgumentException("2D arrays require 1..256 rows/columns and at most 4096 cells.");
        object first=matrix?(columns>0?((object[])rows[0])[0]:null):(rows.Length>0?rows[0]:null);
        if(elementClass==null)elementClass=first is string?"String":first is bool?"Boolean":"Digital";
        Type elementType=elementClass=="Digital"?typeof(double):elementClass=="Boolean"?typeof(bool):elementClass=="String"?typeof(string):null;
        if(elementType==null)throw new ArgumentException("Array defaults support DBL, Boolean or String elements.");
        Array result=matrix?Array.CreateInstance(elementType,rows.Length,columns):Array.CreateInstance(elementType,rows.Length);int textTotal=0;
        for(int r=0;r<rows.Length;r++) {
            object[] cells=matrix?rows[r] as object[]:new[]{rows[r]};if(cells==null||(matrix&&cells.Length!=columns))throw new ArgumentException("2D Array value must be rectangular.");
            for(int c=0;c<cells.Length;c++) {
                object value=cells[c];
                if(elementClass=="Digital")value=Data.Number(value);
                else if(elementClass=="Boolean"){if(!(value is bool))throw new ArgumentException("Boolean Array elements must be JSON booleans.");}
                else {if(!(value is string)||((string)value).Length>16384)throw new ArgumentException("String Array elements require text of at most 16384 characters.");textTotal+=((string)value).Length;if(textTotal>262144)throw new ArgumentException("String Array total text exceeds 262144 characters.");}
                if(matrix)result.SetValue(value,r,c);else result.SetValue(value,r);
            }
        }
        return result;
    }
    public static void ValidateTypedArrayDescriptor(int rank,string elementClass,int[] descriptor) {
        int code=elementClass=="Digital"?0x0A:elementClass=="Boolean"?0x21:elementClass=="String"?0x30:-1;int elementTypeIndex=4+2*rank;
        if(code<0||(rank!=1&&rank!=2)||descriptor==null||descriptor.Length<=elementTypeIndex||(descriptor[1]&255)!=0x40||descriptor[2]!=rank||(descriptor[elementTypeIndex]&255)!=code)throw new ArgumentException("Array defaults require a fresh rank 1/2 Array descriptor matching DBL, Boolean or String element class.");
    }
    public static bool ExactTypedArrayEqual(Array expected,object raw) {
        var actual=raw as Array;if(expected==null||actual==null||expected.GetType()!=actual.GetType()||expected.Rank<1||expected.Rank>2)return false;
        Type element=expected.GetType().GetElementType();if(element!=typeof(bool)&&element!=typeof(string)&&!IsFiniteNumeric(Activator.CreateInstance(element)))return false;
        for(int d=0;d<expected.Rank;d++)if(expected.GetLength(d)!=actual.GetLength(d)||expected.GetLowerBound(d)!=0||actual.GetLowerBound(d)!=0)return false;
        return expected.Cast<object>().Zip(actual.Cast<object>(),(x,y)=>ExactControlValueEqual(x,y)).All(x=>x);
    }
    public static Array ToNativeNumericArray(object raw,Type elementType) {
        if(elementType==null||elementType==typeof(bool)||elementType==typeof(string)||!elementType.IsValueType||!IsFiniteNumeric(Activator.CreateInstance(elementType)))throw new ArgumentException("Actual numeric SAFEARRAY element CLR type required.");
        // Use the existing bounded rectangular JSON check, but convert each
        // original scalar directly; a temporary Double must not round an I64.
        Array shape=ToTypedArray(raw,"Digital");var rows=(object[])raw;bool matrix=shape.Rank==2;
        Array result=matrix?Array.CreateInstance(elementType,shape.GetLength(0),shape.GetLength(1)):Array.CreateInstance(elementType,shape.Length);
        object zero=Activator.CreateInstance(elementType);
        for(int r=0;r<rows.Length;r++) {var cells=matrix?(object[])rows[r]:new[]{rows[r]};for(int c=0;c<cells.Length;c++) {object value=NormalizeControlDefaultInner(cells[c],zero,0);if(matrix)result.SetValue(value,r,c);else result.SetValue(value,r);}}
        return result;
    }
    public static object TypedArrayJson(Array value) {
        if(value==null||value.Rank<1||value.Rank>2)throw new ArgumentException("Rank 1/2 array required.");
        if(value.Rank==1)return value.Cast<object>().ToArray();
        var rows=new object[value.GetLength(0)];for(int r=0;r<rows.Length;r++){var row=new object[value.GetLength(1)];for(int c=0;c<row.Length;c++)row[c]=value.GetValue(r,c);rows[r]=row;}return rows;
    }
    public static Array ToDoubleArray(object raw) {
        var rows=raw as object[];if(rows==null||rows.Length>4096)throw new ArgumentException("Array value must be a bounded JSON array.");
        bool matrix=rows.Length>0&&rows[0] is object[];
        if(!matrix) {var vector=new double[rows.Length];for(int i=0;i<rows.Length;i++)vector[i]=Data.Number(rows[i]);return vector;}
        int columns=((object[])rows[0]).Length;if(rows.Length>256||columns<1||columns>256||rows.Length*columns>4096)throw new ArgumentException("2D arrays require 1..256 rows/columns and at most 4096 cells.");
        var result=new double[rows.Length,columns];
        for(int r=0;r<rows.Length;r++) {var cells=rows[r] as object[];if(cells==null||cells.Length!=columns)throw new ArgumentException("2D Array value must be rectangular.");for(int c=0;c<columns;c++)result[r,c]=Data.Number(cells[c]);}
        return result;
    }
    public static void ValidateDoubleArrayDescriptor(int rank,int[] descriptor) {
        int elementTypeIndex=4+2*rank;
        if((rank!=1&&rank!=2)||descriptor==null||descriptor.Length<=elementTypeIndex||(descriptor[1]&255)!=0x40||descriptor[2]!=rank||(descriptor[elementTypeIndex]&255)!=0x0A)throw new ArgumentException("Array defaults require a fresh rank 1/2 Array descriptor with a scalar DBL element.");
    }
    public static bool ExactDoubleArrayEqual(Array expected,object raw) {
        var actual=raw as Array;
        if(expected==null||actual==null||expected.GetType().GetElementType()!=typeof(double)||actual.GetType().GetElementType()!=typeof(double)||expected.Rank!=actual.Rank||expected.Rank<1||expected.Rank>2)return false;
        for(int d=0;d<expected.Rank;d++)if(expected.GetLength(d)!=actual.GetLength(d)||expected.GetLowerBound(d)!=0||actual.GetLowerBound(d)!=0)return false;
        var left=expected.Cast<double>().ToArray();var right=actual.Cast<double>().ToArray();
        return left.Length==right.Length&&left.Zip(right,(x,y)=>!Double.IsNaN(x)&&!Double.IsInfinity(x)&&!Double.IsNaN(y)&&!Double.IsInfinity(y)&&x==y).All(x=>x);
    }
    public static object DoubleArrayJson(Array value) {
        if(value.Rank==1)return value.Cast<double>().ToArray();
        var rows=new object[value.GetLength(0)];for(int r=0;r<rows.Length;r++){var row=new double[value.GetLength(1)];for(int c=0;c<row.Length;c++)row[c]=(double)value.GetValue(r,c);rows[r]=row;}return rows;
    }
    public static void ValidateMovedArrayDescriptor(int rank,string elementClass,int[] descriptor) {
        if(elementClass=="Cluster") {if(rank!=1||descriptor==null||descriptor.Length<7||(descriptor[1]&255)!=0x40||descriptor[2]!=1||(descriptor[6]&255)!=0x50)throw new ArgumentException("Moved Cluster arrays currently require rank1 and actual Cluster element descriptor.");}
        else if(elementClass=="Digital") {int index=4+2*rank;if((rank!=1&&rank!=2)||descriptor==null||descriptor.Length<=index||(descriptor[1]&255)!=0x40||descriptor[2]!=rank||!NumericDescriptorCodes.Contains(descriptor[index]&255))throw new ArgumentException("Moved Numeric Array requires a fresh rank1/2 descriptor with a supported actual numeric representation.");}
        else ValidateTypedArrayDescriptor(rank,elementClass,descriptor);
    }
    static void ValidateCurrentControlMembership(Dictionary<string,object> helpers,string target,uint control) {
        if(control==0)throw new ArgumentException("Unavailable current Control reference.");
        var panel=PanelIdentities(helpers,target);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint current=control;
        for(int depth=0;depth<16;depth++) {
            string identity=ReadObjectIdentityKey(helpers,target,current);
            if(!seen.Add(identity))throw new InvalidOperationException("Cycle in actual Control owner ancestry.");
            if(panel.Contains(identity))return;
            // Generic Ctl.Owner preserves native class checking and its error chain;
            // a same-VI GObject alone is insufficient evidence of a Control.
            current=Convert.ToUInt32(InvokeHelper(helpers["Own20261012_PublicGetControlOwner_v1"],new Dictionary<string,object>{{"control_id",current}},new[]{"owner_id"})["owner_id"]);
            if(current==0)throw new ArgumentException("Control does not descend from a current target front-panel Control.");
        }
        throw new ArgumentException("Control owner ancestry exceeds the supported nesting bound.");
    }
    static HashSet<string> PanelIdentities(Dictionary<string,object> helpers,string target) {
        var panel=InvokeHelper(helpers["Native_GetPanelControls"],new Dictionary<string,object>{{"target_path",target}},new[]{"control_ids"});return new HashSet<string>(((IEnumerable)panel["control_ids"]).Cast<object>().Select(x=>ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(x))),StringComparer.OrdinalIgnoreCase);
    }
    static Dictionary<string,object> MoveArrayElement(Dictionary<string,object> helpers,string target,uint source,uint owner,short x,short y,Dictionary<string,string> arrays) {
        string sourceKey=ReadObjectIdentityKey(helpers,target,source),ownerKey=ReadObjectIdentityKey(helpers,target,owner);
        if(sourceKey==ownerKey||!arrays.ContainsKey(ownerKey)||arrays[ownerKey]!=null)throw new ArgumentException("Array move requires a different empty Array created in this recipe.");
        string kind=Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",source}},new[]{"class_name"})["class_name"]);
        string ownerKind=Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",owner}},new[]{"class_name"})["class_name"]);
        if(ownerKind!="Array"||!new[]{"Digital","Boolean","String","Cluster"}.Contains(kind))throw new ArgumentException("Only verified Digital, Boolean, String or Cluster controls may become Array elements.");
        var sourceTerminal=InvokeHelper(helpers["Native_GetControlTerminal"],new Dictionary<string,object>{{"control_id",source}},new[]{"terminal_id"});int[] sourceType=((IEnumerable)InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",sourceTerminal["terminal_id"]}},new[]{"type_string"})["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();
        uint? sourceRepresentation=null;
        if(kind=="Digital") {sourceRepresentation=ValidateNumericRepresentation(InvokeHelper(helpers["Own20261012_GetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",source}},new[]{"representation"})["representation"]);ValidateNumericDescriptor(sourceRepresentation.Value,sourceType);}
        else {int sourceCode=kind=="Boolean"?0x21:kind=="String"?0x30:0x50;if(sourceType.Length<2||(sourceType[1]&255)!=sourceCode)throw new ArgumentException("Source control has an unaccepted actual scalar/Cluster representation.");}
        var before=PanelIdentities(helpers,target);if(!before.Contains(sourceKey)||!before.Contains(ownerKey))throw new ArgumentException("Array move requires both objects to be current top-level controls.");
        var sd=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",source}},new[]{"is_indicator"})["is_indicator"];
        var od=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",owner}},new[]{"is_indicator"})["is_indicator"];
        if(!(sd is bool)||!(od is bool))throw new ArgumentException("Moved element and destination Array direction must use the native Boolean ABI.");
        // GetArrayInfo's tested empty-Array contract is NI error1054 and a zero
        // element. Do not accept any other error or rely on a cached refnum.
        object getter=helpers["Own20261012_GetArrayInfo"];LabView.Invoke(getter,"SetControlValue",BindingFlags.InvokeMethod,"array_id",owner);LabView.Invoke(getter,"Run",BindingFlags.InvokeMethod,false);
        var error=LabView.Invoke(getter,"GetControlValue",BindingFlags.InvokeMethod,"error out") as IList;
        object element=LabView.Invoke(getter,"GetControlValue",BindingFlags.InvokeMethod,"element_id");
        if(error==null||error.Count!=3||!(error[0] is bool)||!(bool)error[0]||Convert.ToInt32(error[1])!=1054||Convert.ToUInt32(element)!=0)throw new ArgumentException("Destination must be a natively empty Array (error1054 and null element); no element was moved.");
        InvokeHelper(helpers["Own20261012_MoveControlToOwner"],new Dictionary<string,object>{{"control_id",source},{"owner_id",owner},{"x",x},{"y",y}},new string[0]);
        // Move can adopt the destination's previous direction. After the Array
        // has a real element/type, make the container inherit the source's
        // original direction and reacquire all descendant/terminal references.
        object movedOwnerDirection=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",owner}},new[]{"is_indicator"})["is_indicator"];
        if(!(movedOwnerDirection is bool))throw new InvalidOperationException("Moved Array direction did not use the native Boolean ABI.");
        if((bool)movedOwnerDirection!=(bool)sd)InvokeHelper(helpers["Own20261012_SetControlIndicator"],new Dictionary<string,object>{{"control_id",owner},{"is_indicator",sd}},new string[0]);
        object inheritedDirection=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",owner}},new[]{"is_indicator"})["is_indicator"];
        if(!(inheritedDirection is bool)||(bool)inheritedDirection!=(bool)sd)throw new InvalidOperationException("Array did not inherit the moved source direction.");
        var info=ReadArrayInfo(helpers,owner);uint fresh=Convert.ToUInt32(info["element_id"]);if(ReadObjectIdentityKey(helpers,target,fresh)!=sourceKey)throw new InvalidOperationException("Moved element identity differs.");
        var after=PanelIdentities(helpers,target);before.Remove(sourceKey);if(!after.SetEquals(before)||!after.Contains(ownerKey))throw new InvalidOperationException("Panel membership changed beyond the moved control.");
        object direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",fresh}},new[]{"is_indicator"})["is_indicator"];if(!(direction is bool)||(bool)direction!=(bool)sd)throw new InvalidOperationException("Moved element direction changed.");
        int[] descriptor=((IEnumerable)InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",info["terminal_id"]}},new[]{"type_string"})["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();ValidateMovedArrayDescriptor(Convert.ToInt32(info["dimensions"]),kind,descriptor);
        if(sourceRepresentation.HasValue) {uint actual=ValidateNumericRepresentation(InvokeHelper(helpers["Own20261012_GetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",fresh}},new[]{"representation"})["representation"]);if(actual!=sourceRepresentation.Value)throw new InvalidOperationException("Moved Numeric representation changed.");int index=4+2*Convert.ToInt32(info["dimensions"]);if((descriptor[index]&255)!=NumericDescriptorCodes[actual])throw new InvalidOperationException("Moved Numeric Array element descriptor differs from the original representation.");}
        arrays[ownerKey]=sourceKey;info["class_name"]=kind;info["element_identity"]=sourceKey;info["is_indicator"]=direction;info["type_descriptor"]=descriptor;return info;
    }
    static Dictionary<string,object> BooleanSnapshot(Dictionary<string,object> helpers,object vi,string target,uint id) {
        ReadObjectIdentityKey(helpers,target,id);
        var data=InvokeHelper(helpers["Own20261012_GetBooleanStyleInfo"],new Dictionary<string,object>{{"boolean_id",id}},new[]{"text","multiple_strings","mechanical_action","state_strings"});
        string[] states=TextArray(data["state_strings"]);if((states.Length!=1&&states.Length!=4)||!(data["text"] is string)||!(data["multiple_strings"] is bool))throw new InvalidOperationException("Boolean text metadata differs from the accepted String[]/Boolean ABI.");
        var independent=InvokeHelper(helpers["Own20261012_GetBooleanText"],new Dictionary<string,object>{{"boolean_id",id}},new[]{"text"});if(!Object.Equals(independent["text"],data["text"]))throw new InvalidOperationException("Independent Boolean text readback differs.");
        string label=Convert.ToString(InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",id}},new[]{"label"})["label"]);
        if(!SameObjectIdentity(helpers,target,id,Convert.ToUInt32(FindControl(helpers,target,label)["control_id"])))throw new ArgumentException("Boolean text currently requires the same unique top-level control identity.");
        object direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",id}},new[]{"is_indicator"})["is_indicator"],value=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label);
        if(!(direction is bool)||!(value is bool))throw new InvalidOperationException("Boolean direction/value did not have Boolean ABI.");data["label"]=label;data["is_indicator"]=direction;data["value"]=value;return data;
    }
    public static void ValidateBooleanTextReadback(Dictionary<string,object> before,Dictionary<string,object> after,string requested) {
        foreach(string field in new[]{"label","is_indicator","value","multiple_strings","mechanical_action"})if(!ExactControlValueEqual(before[field],after[field]))throw new InvalidOperationException("Boolean text edit changed "+field+".");
        string[] previous=TextArray(before["state_strings"]),current=TextArray(after["state_strings"]);if((previous.Length!=1&&previous.Length!=4)||current.Length!=previous.Length)throw new InvalidOperationException("Boolean state string shape changed.");
        if(String.IsNullOrEmpty(requested)) {if(!previous.SequenceEqual(current,StringComparer.Ordinal)||!Object.Equals(before["text"],after["text"]))throw new InvalidOperationException("Empty Boolean text must preserve native strings.");}
        else if(current.Any(s=>s!=requested)||!Object.Equals(after["text"],requested))throw new InvalidOperationException("Boolean text was not updated in every state.");
    }
    static Dictionary<string,object> ApplyBooleanText(Dictionary<string,object> helpers,object vi,string target,uint id,string requested) {
        var before=BooleanSnapshot(helpers,vi,target,id);bool change=!String.IsNullOrEmpty(requested);
        if(change)InvokeHelper(helpers["Own20261012_SetBooleanStateStrings"],new Dictionary<string,object>{{"boolean_id",id},{"state_strings",Enumerable.Repeat(requested,TextArray(before["state_strings"]).Length).ToArray()}},new string[0]);
        var after=change?BooleanSnapshot(helpers,vi,target,id):before;ValidateBooleanTextReadback(before,after,requested);after["changed"]=change;return after;
    }
    static Dictionary<string,object> ReadArrayInfo(Dictionary<string,object> helpers,uint id) {
        var result=InvokeHelper(helpers["Own20261012_GetArrayInfo"],new Dictionary<string,object>{{"array_id",id}},new[]{"dimensions","element_id","terminal_id"});
        if(Convert.ToUInt32(result["element_id"])==0||Convert.ToUInt32(result["terminal_id"])==0)throw new InvalidOperationException("Array lacks a valid element or Terminal.");return result;
    }
    public static void ValidateLoopLabelClass(string kind) {
        if(kind!="ForLoop"&&kind!="WhileLoop")throw new ArgumentException("Node label requires an actual ForLoop or WhileLoop; no typed label setter was invoked.");
    }
    public static uint[] MatchInsideTerminalsToFrames(string[] frameIdentities,string[] insideOwners,uint[] insideIds) {
        if(frameIdentities==null||insideOwners==null||insideIds==null||frameIdentities.Length<1||frameIdentities.Length>1000||frameIdentities.Length!=insideIds.Length||insideOwners.Length!=insideIds.Length||frameIdentities.Concat(insideOwners).Any(String.IsNullOrWhiteSpace)||insideIds.Any(x=>x==0))throw new ArgumentException("Current frame identities and nonzero inside terminals must have exact coverage.");
        if(frameIdentities.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=frameIdentities.Length||insideOwners.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=insideOwners.Length||insideIds.Distinct().Count()!=insideIds.Length)throw new ArgumentException("Duplicate frame, inside owner or terminal reference.");
        var byOwner=new Dictionary<string,uint>(StringComparer.OrdinalIgnoreCase);for(int i=0;i<insideIds.Length;i++)byOwner.Add(insideOwners[i],insideIds[i]);
        if(frameIdentities.Any(x=>!byOwner.ContainsKey(x)))throw new ArgumentException("Inside terminal owner does not match every actual current frame.");return frameIdentities.Select(x=>byOwner[x]).ToArray();
    }
    static int[] NativeTerminalDescriptor(Dictionary<string,object> helpers,uint id) {
        var value=InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",id}},new[]{"type_string"})["type_string"];var values=value as IEnumerable;if(values==null||value is string)throw new InvalidOperationException("Native Terminal descriptor array required.");int[] descriptor=values.Cast<object>().Select(Convert.ToInt32).ToArray();if(descriptor.Length<2)throw new InvalidOperationException("Native Terminal has no current descriptor.");return descriptor;
    }
    static string CheckInsideTerminal(Dictionary<string,object> helpers,string target,uint terminal,uint diagram,bool source,int typecode) {
        if(terminal==0||diagram==0)throw new InvalidOperationException("Unavailable current Terminal or Diagram.");string identity=ReadObjectIdentityKey(helpers,target,terminal),owner=ReadObjectIdentityKey(helpers,target,diagram);
        var context=InvokeHelper(helpers["Own20261012_GetTerminalContext"],new Dictionary<string,object>{{"terminal_id",terminal}},new[]{"is_source","diagram_id"});if(!(context["is_source"] is bool)||(bool)context["is_source"]!=source||ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(context["diagram_id"]))!=owner||(NativeTerminalDescriptor(helpers,terminal)[1]&255)!=typecode)throw new InvalidOperationException("Current Terminal direction, owning Diagram or scalar descriptor differs.");return identity;
    }
    static Dictionary<string,object> ReadForCountInfo(Dictionary<string,object> helpers,string target,uint id) {
        if(id==0)throw new ArgumentException("Unavailable ForLoop reference.");ReadObjectIdentityKey(helpers,target,id);string kind=Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",id}},new[]{"class_name"})["class_name"]);if(kind!="ForLoop")throw new ArgumentException("For count metadata requires actual ForLoop before its typed helper call.");
        var structure=InvokeHelper(helpers["Own20261012_GetStructureInfo"],new Dictionary<string,object>{{"structure_id",id}},new[]{"owner_id","diagram_ids"});uint[] diagrams=NativeRefs(structure["diagram_ids"]);if(diagrams.Length!=1)throw new InvalidOperationException("ForLoop must expose exactly one actual body Diagram.");uint parent=Convert.ToUInt32(structure["owner_id"]);ReadObjectIdentityKey(helpers,target,parent);ReadObjectIdentityKey(helpers,target,diagrams[0]);
        var sides=InvokeHelper(helpers["Own20261012_PublicGetForCountSides_v2"],new Dictionary<string,object>{{"for_id",id}},new[]{"tunnel_id","inside_ids","outside_id"});uint[] inside=NativeRefs(sides["inside_ids"]);uint outside=Convert.ToUInt32(sides["outside_id"]),tunnel=Convert.ToUInt32(sides["tunnel_id"]);if(inside.Length!=1||tunnel==0)throw new InvalidOperationException("For count requires one actual inner N and count tunnel.");ReadObjectIdentityKey(helpers,target,tunnel);
        uint counter=Convert.ToUInt32(InvokeHelper(helpers["Native_GetForTerminals"],new Dictionary<string,object>{{"for_id",id}},new[]{"counter_id"})["counter_id"]);string outerIdentity=CheckInsideTerminal(helpers,target,outside,parent,false,3),innerIdentity=CheckInsideTerminal(helpers,target,inside[0],diagrams[0],true,3),counterIdentity=CheckInsideTerminal(helpers,target,counter,diagrams[0],true,3);if(new[]{outerIdentity,innerIdentity,counterIdentity}.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)throw new InvalidOperationException("Inner N, outer N and counter i must be different physical Terminals.");
        return new Dictionary<string,object>{{"count_id",outside},{"inside_count_id",inside[0]},{"counter_id",counter},{"diagram_id",diagrams[0]},{"tunnel_id",tunnel},{"type_descriptor",NativeTerminalDescriptor(helpers,outside)}};
    }
    static Dictionary<string,object> ReadCaseSelectorInfo(Dictionary<string,object> helpers,string target,uint id) {
        if(id==0)throw new ArgumentException("Unavailable CaseStructure reference.");ReadObjectIdentityKey(helpers,target,id);string kind=Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",id}},new[]{"class_name"})["class_name"]);if(kind!="CaseStructure")throw new ArgumentException("Case selector metadata requires actual CaseStructure before its typed helper call.");
        var structure=InvokeHelper(helpers["Own20261012_GetStructureInfo"],new Dictionary<string,object>{{"structure_id",id}},new[]{"owner_id"});uint parent=Convert.ToUInt32(structure["owner_id"]);ReadObjectIdentityKey(helpers,target,parent);
        uint[] frames=NativeRefs(InvokeHelper(helpers["Native_GetCaseInfo_v2"],new Dictionary<string,object>{{"case_id",id}},new[]{"frame_ids"})["frame_ids"]);string[] frameIdentities=frames.Select(x=>ReadObjectIdentityKey(helpers,target,x)).ToArray();
        var sides=InvokeHelper(helpers["Own20261012_PublicGetCaseSelectorSides_v2"],new Dictionary<string,object>{{"case_id",id}},new[]{"tunnel_id","inside_ids","outside_id"});uint[] inside=NativeRefs(sides["inside_ids"]);uint outside=Convert.ToUInt32(sides["outside_id"]),tunnel=Convert.ToUInt32(sides["tunnel_id"]);if(outside==0||tunnel==0)throw new InvalidOperationException("Current Case selector tunnel/outside Terminal unavailable.");ReadObjectIdentityKey(helpers,target,tunnel);
        int[] descriptor=NativeTerminalDescriptor(helpers,outside);int typecode=descriptor[1]&255;if(typecode!=3&&typecode!=48&&typecode!=33)throw new ArgumentException("Case inside selector currently accepts independently tested scalar Boolean, I32 and String only.");string outerIdentity=CheckInsideTerminal(helpers,target,outside,parent,false,typecode);
        string[] owners=inside.Select(x=>ReadObjectIdentityKey(helpers,target,Convert.ToUInt32(InvokeHelper(helpers["Own20261012_GetTerminalContext"],new Dictionary<string,object>{{"terminal_id",x}},new[]{"diagram_id"})["diagram_id"]))).ToArray();uint[] ordered=MatchInsideTerminalsToFrames(frameIdentities,owners,inside);string[] physical=ordered.Select((x,i)=>CheckInsideTerminal(helpers,target,x,frames[i],true,typecode)).ToArray();if(physical.Concat(new[]{outerIdentity}).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=physical.Length+1)throw new InvalidOperationException("Case inside/outside selector Terminals must be physically distinct.");
        return new Dictionary<string,object>{{"frame_ids",frames},{"inside_ids",ordered},{"outside_id",outside},{"selector_tunnel_id",tunnel},{"type_descriptor",descriptor},{"frame_identities",frameIdentities}};
    }
    public static object[] NativeObjectBounds(object raw) {
        var values=raw as IEnumerable;var array=raw as Array;if(values==null||raw is string||raw is IDictionary||(array!=null&&array.Rank!=1))throw new InvalidOperationException("Native GObject bounds must be a one-dimensional size array.");
        object[] bounds=values.Cast<object>().ToArray();if(bounds.Length!=2||bounds.Any(x=>!IsFiniteNumeric(x)||Convert.ToDouble(x)<0))throw new InvalidOperationException("Native GObject bounds must be a two-value nonnegative size.");return bounds;
    }
    // The delegates also make empty-label and primary-error behavior testable
    // without NI. Native ownership/class checks happen before this policy.
    public static Dictionary<string,object> ApplyNodeLabelPolicy(string requested,Action<string> setText,Action<bool> setVisible,Func<Dictionary<string,object>> read) {
        bool change=!String.IsNullOrEmpty(requested);
        if(change){setText(requested);setVisible(true);}
        var result=read();
        if(!(result["label"] is string)||!(result["visible"] is bool))throw new InvalidOperationException("Node label getter ABI must return String and Boolean.");
        if(change&&(!String.Equals((string)result["label"],requested,StringComparison.Ordinal)||!(bool)result["visible"]))throw new InvalidOperationException("Node label text/visibility readback differs from the requested value.");
        result["changed"]=change;return result;
    }
    static Dictionary<string,object> LoopLabelSnapshot(Dictionary<string,object> helpers,string target,uint id) {
        string identity=ReadObjectIdentityKey(helpers,target,id);
        var info=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",id}},new[]{"class_name","bounds"});
        ValidateLoopLabelClass(Convert.ToString(info["class_name"]));
        var input=new Dictionary<string,object>{{"node_id",id}};
        var text=InvokeHelper(helpers["Own20261012_PublicGetNodeLabel_v1"],input,new[]{"label"});
        var visible=InvokeHelper(helpers["Own20261012_PublicGetNodeLabelVisible_v1"],input,new[]{"visible"});
        if(!(text["label"] is string)||!(visible["visible"] is bool))throw new InvalidOperationException("Node label getter ABI must return String and Boolean.");
        object[] bounds=NativeObjectBounds(info["bounds"]);
        return new Dictionary<string,object>{{"label",text["label"]},{"visible",visible["visible"]},{"bounds",bounds},{"class_name",info["class_name"]},{"identity",identity}};
    }
    static Dictionary<string,object> ApplyNodeLabel(Dictionary<string,object> helpers,string target,uint id,string requested) {
        // Verify current target Path+UID and actual native class before any typed
        // property setter. The post-write snapshot repeats both checks.
        string before=ReadObjectIdentityKey(helpers,target,id);
        var kind=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",id}},new[]{"class_name"});ValidateLoopLabelClass(Convert.ToString(kind["class_name"]));
        var result=ApplyNodeLabelPolicy(requested,
            value=>InvokeHelper(helpers["Own20261012_PublicSetNodeLabel_v1"],new Dictionary<string,object>{{"node_id",id},{"label",value}},new string[0]),
            value=>InvokeHelper(helpers["Own20261012_PublicSetNodeLabelVisible_v1"],new Dictionary<string,object>{{"node_id",id},{"visible",value}},new string[0]),
            ()=>LoopLabelSnapshot(helpers,target,id));
        if(!String.Equals(before,(string)result["identity"],StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Node label operation changed the target object identity.");
        return result;
    }
    public static bool SameObjectIdentityData(string target,int leftUid,string leftPath,int rightUid,string rightPath) {
        if(String.IsNullOrWhiteSpace(target)||String.IsNullOrWhiteSpace(leftPath)||String.IsNullOrWhiteSpace(rightPath)||!Path.IsPathRooted(target)||!Path.IsPathRooted(leftPath)||!Path.IsPathRooted(rightPath))return false;
        string expected=Path.GetFullPath(target),left=Path.GetFullPath(leftPath),right=Path.GetFullPath(rightPath);
        if(!String.Equals(Path.GetPathRoot(target),Path.GetPathRoot(expected),StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetPathRoot(leftPath),Path.GetPathRoot(left),StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetPathRoot(rightPath),Path.GetPathRoot(right),StringComparison.OrdinalIgnoreCase))return false;
        return leftUid==rightUid&&String.Equals(left,expected,StringComparison.OrdinalIgnoreCase)&&String.Equals(right,expected,StringComparison.OrdinalIgnoreCase);
    }
    public static string ObjectIdentityKey(string target,int uid,string owningPath) {
        if(!SameObjectIdentityData(target,uid,owningPath,uid,owningPath))throw new ArgumentException("Container object does not belong to the current target VI.");
        return Path.GetFullPath(target)+"|UID:"+uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    static string ReadObjectIdentityKey(Dictionary<string,object> helpers,string target,uint reference) {
        var result=InvokeHelper(helpers["Own20261012_GetObjectIdentity"],new Dictionary<string,object>{{"object_id",reference}},new[]{"uid","vi_path"});
        if(!(result["uid"] is int)||!(result["vi_path"] is string))throw new InvalidOperationException("Object identity ABI does not match I32 UID and String owner path.");
        return ObjectIdentityKey(target,(int)result["uid"],(string)result["vi_path"]);
    }
    static void RemoveContainerIdentity(string removed,Dictionary<string,List<string>> members,Dictionary<string,string> classes,Dictionary<string,string> arrays) {
        var dead=new HashSet<string>(StringComparer.OrdinalIgnoreCase){removed};var pending=new Queue<string>();pending.Enqueue(removed);
        while(pending.Count>0) {string next=pending.Dequeue();List<string> children;string element;
            if(members.TryGetValue(next,out children))foreach(string child in children)if(dead.Add(child))pending.Enqueue(child);
            if(arrays.TryGetValue(next,out element)&&element!=null&&dead.Add(element))pending.Enqueue(element);
        }
        // A changed parent layout becomes unknown; surviving siblings are never
        // relabeled or silently reordered from their old tuple positions.
        foreach(string key in members.Where(x=>dead.Contains(x.Key)||x.Value.Any(dead.Contains)).Select(x=>x.Key).ToArray())members.Remove(key);
        foreach(string key in arrays.Where(x=>dead.Contains(x.Key)||(x.Value!=null&&dead.Contains(x.Value))).Select(x=>x.Key).ToArray())arrays.Remove(key);
        foreach(string key in dead)classes.Remove(key);
    }
    static bool SameObjectIdentity(Dictionary<string,object> helpers,string target,uint left,uint right) {
        // NI can issue distinct refnums for the same object. UID is only unique
        // inside a VI, so both independently read owner paths must equal target.
        var a=InvokeHelper(helpers["Own20261012_GetObjectIdentity"],new Dictionary<string,object>{{"object_id",left}},new[]{"uid","vi_path"});
        var b=InvokeHelper(helpers["Own20261012_GetObjectIdentity"],new Dictionary<string,object>{{"object_id",right}},new[]{"uid","vi_path"});
        if(!(a["uid"] is int)||!(b["uid"] is int)||!(a["vi_path"] is string)||!(b["vi_path"] is string))throw new InvalidOperationException("Object identity ABI does not match I32 UID and String owner path.");
        return SameObjectIdentityData(target,(int)a["uid"],(string)a["vi_path"],(int)b["uid"],(string)b["vi_path"]);
    }
    static Dictionary<string,object> ApplyArrayDefault(Dictionary<string,object> helpers,object vi,string target,uint id,object requested) {
        var named=InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",id}},new[]{"label"});string label=Convert.ToString(named["label"]);
        if(String.IsNullOrWhiteSpace(label)||!SameObjectIdentity(helpers,target,id,Convert.ToUInt32(FindControl(helpers,target,label)["control_id"])))throw new ArgumentException("Array default requires the same unique top-level control object identity.");
        var direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",id}},new[]{"is_indicator"});if(!(direction["is_indicator"] is bool))throw new ArgumentException("Array default direction must use the actual Boolean ABI.");
        var info=ReadArrayInfo(helpers,id);int rank=Convert.ToInt32(info["dimensions"]);
        var elementInfo=InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",info["element_id"]}},new[]{"class_name"});string elementClass=Convert.ToString(elementInfo["class_name"]);if(!new[]{"Digital","Boolean","String"}.Contains(elementClass))throw new ArgumentException("Array default element must be Digital, Boolean or String.");
        object current=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label);var currentArray=current as Array;
        if(currentArray==null||currentArray.Rank!=rank)throw new InvalidOperationException("Actual native current SAFEARRAY rank differs from NumDims.");
        var type=InvokeHelper(helpers["Native_GetTerminalTypeDescriptor"],new Dictionary<string,object>{{"terminal_id",info["terminal_id"]}},new[]{"type_string"});int[] descriptor=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();
        Array typed;
        if(elementClass=="Digital") {ValidateMovedArrayDescriptor(rank,elementClass,descriptor);uint representation=ValidateNumericRepresentation(InvokeHelper(helpers["Own20261012_GetNumericRepresentation"],new Dictionary<string,object>{{"numeric_id",info["element_id"]}},new[]{"representation"})["representation"]);int index=4+2*rank;if((descriptor[index]&255)!=NumericDescriptorCodes[representation])throw new InvalidOperationException("Array element representation differs from its actual terminal TD.");typed=ToNativeNumericArray(requested,currentArray.GetType().GetElementType());}
        else {ValidateTypedArrayDescriptor(rank,elementClass,descriptor);typed=ToTypedArray(requested,elementClass);}
        if(typed.Rank!=rank)throw new ArgumentException("Array value rank differs from the current NumDims.");
        if(currentArray==null||currentArray.Rank!=rank||currentArray.GetType()!=typed.GetType())throw new InvalidOperationException("Native Array current value is not a matching typed SAFEARRAY: expected "+typed.GetType().FullName+", actual "+(current==null?"null":current.GetType().FullName));
        LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,label,typed);
        object actual=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label);if(!ExactTypedArrayEqual(typed,actual))throw new InvalidOperationException("Array rank, shape, type or finite value readback differs; defaults were not committed.");
        if(!Object.Equals(InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",id}},new[]{"is_indicator"})["is_indicator"],direction["is_indicator"]))throw new InvalidOperationException("Array default changed the control direction.");
        LabView.Invoke(vi,"MakeCurValueDefault",BindingFlags.InvokeMethod);
        return new Dictionary<string,object>{{"value",TypedArrayJson((Array)actual)},{"dimensions",rank},{"shape",Enumerable.Range(0,rank).Select(typed.GetLength).ToArray()},{"value_clr_type",actual.GetType().FullName},{"class_name","Array"},{"label",label},{"type_descriptor",descriptor}};
    }
    static string ReadArrayElementClass(Dictionary<string,object> helpers,uint id) {
        var info=ReadArrayInfo(helpers,id);
        return Convert.ToString(InvokeHelper(helpers["Native_GetObjectInfo"],new Dictionary<string,object>{{"object_id",info["element_id"]}},new[]{"class_name"})["class_name"]);
    }
    static void CommitFinalDefaults(Dictionary<string,object> helpers,object vi,string target,Dictionary<string,object> pending) {
        if(pending.Count==0)return;
        var live=new Dictionary<string,object>(StringComparer.Ordinal);
        var labels=new List<string>();
        var panel=InvokeHelper(helpers["Native_GetPanelControls"],new Dictionary<string,object>{{"target_path",target}},new[]{"control_ids"});
        foreach(object raw in (IEnumerable)panel["control_ids"]) {
            uint id=Convert.ToUInt32(raw);object value;
            string label=Convert.ToString(InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",id}},new[]{"label"})["label"]);
            labels.Add(label);
            if(!pending.TryGetValue(ReadObjectIdentityKey(helpers,target,id),out value))continue;
            if(String.IsNullOrWhiteSpace(label)||live.ContainsKey(label))throw new InvalidOperationException("Final default target requires unique nonempty labels.");
            live.Add(label,value);
        }
        if(live.Keys.Any(label=>labels.Count(x=>String.Equals(label,x,StringComparison.Ordinal))!=1))throw new InvalidOperationException("Final default label is ambiguous.");
        foreach(var item in live)LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,item.Key,item.Value);
        foreach(var item in live)if(!ExactDefaultValueEqual(item.Value,LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,item.Key)))throw new InvalidOperationException("Final current value differs: "+item.Key);
        LabView.Invoke(vi,"MakeCurValueDefault",BindingFlags.InvokeMethod);
        foreach(var item in live)if(!ExactDefaultValueEqual(item.Value,LabView.Invoke(vi,"_GetCtrlDefaultValVariant",BindingFlags.InvokeMethod,item.Key)))throw new InvalidOperationException("Native default was not committed: "+item.Key);
    }
    static bool ExactDefaultValueEqual(object expected,object actual) {
        var array=expected as Array;
        return array!=null&&array.GetType().GetElementType()!=typeof(object)?ExactTypedArrayEqual(array,actual):ExactControlValueEqual(expected,actual);
    }
    public static void ValidateControlValue(object value) {int count=0;ValidateControlValue(value,0,ref count);}
    static void ValidateControlValue(object value,int depth,ref int count) {
        if(++count>512||depth>8)throw new ArgumentException("Control value exceeds 512 nodes or depth 8.");
        if(value is bool)return;
        if(value is string) {if(((string)value).Length>16384)throw new ArgumentException("Control text exceeds 16384 characters.");return;}
        var list=value as object[];
        if(list!=null) {if(list.Length>64)throw new ArgumentException("Cluster value exceeds 64 elements.");foreach(object item in list)ValidateControlValue(item,depth+1,ref count);return;}
        Data.Number(value);
    }
    static bool IsFiniteNumeric(object value) {
        if(value==null||value is bool||value is char||value is string)return false;
        switch(Type.GetTypeCode(value.GetType())) {
        case TypeCode.Byte:case TypeCode.SByte:case TypeCode.Int16:case TypeCode.UInt16:case TypeCode.Int32:case TypeCode.UInt32:case TypeCode.Int64:case TypeCode.UInt64:case TypeCode.Decimal:case TypeCode.Single:case TypeCode.Double:
            double n=Convert.ToDouble(value);return !Double.IsNaN(n)&&!Double.IsInfinity(n);
        default:return false;
        }
    }
    public static bool ExactControlValueEqual(object expected,object actual) {
        if(expected is bool||actual is bool)return expected is bool&&actual is bool&&(bool)expected==(bool)actual;
        if(expected is string||actual is string)return expected is string&&actual is string&&String.Equals((string)expected,(string)actual,StringComparison.Ordinal);
        var ea=expected as Array;var aa=actual as Array;
        if(ea!=null&&ea.Rank>1||aa!=null&&aa.Rank>1){if(ea==null||aa==null||ea.Rank!=aa.Rank)return false;for(int d=0;d<ea.Rank;d++)if(ea.GetLength(d)!=aa.GetLength(d)||ea.GetLowerBound(d)!=aa.GetLowerBound(d))return false;return ea.Cast<object>().Zip(aa.Cast<object>(),(x,y)=>ExactControlValueEqual(x,y)).All(x=>x);}
        var a=expected as IList;var b=actual as IList;
        if(a!=null||b!=null) {if(a==null||b==null||a.Count!=b.Count)return false;for(int i=0;i<a.Count;i++)if(!ExactControlValueEqual(a[i],b[i]))return false;return true;}
        if(!IsFiniteNumeric(expected)||!IsFiniteNumeric(actual))return false;
        bool af=expected is double||expected is float,bf=actual is double||actual is float;
        if(af&&bf)return Convert.ToDouble(expected)==Convert.ToDouble(actual);
        try{return Convert.ToDecimal(expected)==Convert.ToDecimal(actual);}catch(OverflowException){return false;}
    }
    public static object NormalizeControlDefault(object requested,object current) {
        ValidateControlValue(requested);
        return NormalizeControlDefaultInner(requested,current,0);
    }
    static object NormalizeControlDefaultInner(object requested,object current,int depth) {
        if(depth>8)throw new ArgumentException("Control value depth exceeds 8.");
        if(current is bool) {if(!(requested is bool))throw new ArgumentException("Boolean control value must be Boolean.");return requested;}
        if(current is string) {if(!(requested is string))throw new ArgumentException("String control value must be text.");return requested;}
        var members=current as object[];
        if(members!=null) {var values=requested as object[];if(values==null||values.Length!=members.Length)throw new ArgumentException("Cluster value must match the current element count and order.");return values.Select((x,i)=>NormalizeControlDefaultInner(x,members[i],depth+1)).ToArray();}
        if(!IsFiniteNumeric(current))throw new ArgumentException("Unsupported current control value type.");
        Data.Number(requested);
        object typed;
        try{typed=Convert.ChangeType(requested,current.GetType(),System.Globalization.CultureInfo.InvariantCulture);}catch(Exception ex){if(ex is OverflowException||ex is InvalidCastException||ex is FormatException)throw new ArgumentException("Value cannot be represented by the current numeric control.",ex);throw;}
        if(!IsFiniteNumeric(typed)||!ExactControlValueEqual(requested,typed))throw new ArgumentException("Numeric value would be coerced by the current control representation.");
        return typed;
    }
    static void VerifyClusterValueLayout(string id,object value,Dictionary<string,List<string>> members,Dictionary<string,string> classes,int depth) {
        if(depth>8||!members.ContainsKey(id))throw new ArgumentException("Cluster defaults require a known member tree created by this recipe; unknown/copied Cluster layouts are not yet supported.");
        var values=value as object[];var children=members[id];
        if(values==null||values.Length!=children.Count)throw new ArgumentException("Current Cluster layout differs from its verified recipe member tree.");
        for(int i=0;i<children.Count;i++) {
            string kind;if(!classes.TryGetValue(children[i],out kind))throw new ArgumentException("Unknown Cluster member class.");
            if(kind=="Cluster")VerifyClusterValueLayout(children[i],values[i],members,classes,depth+1);
            else if(kind=="Digital"?!IsFiniteNumeric(values[i]):kind=="String"?!(values[i] is string):kind=="Boolean"?!(values[i] is bool):true)throw new ArgumentException("Current Cluster member value does not match a supported scalar class.");
        }
    }
    static Dictionary<string,object> ApplyControlDefault(Dictionary<string,object> helpers,object vi,string target,uint id,string kind,object requested,Dictionary<string,List<string>> members,Dictionary<string,string> classes) {
        var named=InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",id}},new[]{"label"});
        string label=Convert.ToString(named["label"]);if(String.IsNullOrWhiteSpace(label))throw new ArgumentException("Default target requires a unique nonempty top-level label.");
        var resolved=FindControl(helpers,target,label);
        if(!SameObjectIdentity(helpers,target,id,Convert.ToUInt32(resolved["control_id"])))throw new ArgumentException("Default target does not match its unique top-level control object identity.");
        var direction=InvokeHelper(helpers["Own20261012_GetControlIndicator"],new Dictionary<string,object>{{"control_id",id}},new[]{"is_indicator"});
        if(!(direction["is_indicator"] is bool))throw new InvalidOperationException("Control direction ABI is not Boolean.");
        ValidateDefaultDirection(kind,(bool)direction["is_indicator"]);
        object current=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label);
        if(kind=="Cluster")VerifyClusterValueLayout(ReadObjectIdentityKey(helpers,target,id),current,members,classes,0);
        else if(kind=="Digital"||kind=="Slide"?!IsFiniteNumeric(current):kind=="String"?!(current is string):kind=="Boolean"?!(current is bool):true)throw new ArgumentException("Default target has an unsupported current value shape.");
        object typed=NormalizeControlDefault(requested,current);
        LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,label,typed);
        object actual=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label);
        if(!ExactControlValueEqual(typed,actual))throw new InvalidOperationException("Control value readback differs from the requested value; defaults were not committed.");
        LabView.Invoke(vi,"MakeCurValueDefault",BindingFlags.InvokeMethod);
        return new Dictionary<string,object>{{"value",actual},{"class_name",kind},{"label",label}};
    }
    public static void ValidateDefaultDirection(string kind,bool isIndicator) {
        if(isIndicator&&kind!="Digital"&&kind!="Slide"&&kind!="Boolean")throw new ArgumentException("Indicator defaults currently require Digital, Slide or Boolean; other output control types are not yet accepted.");
    }
    static string[] TextArray(object raw) {
        var values=Data.Array(raw);if(values.Any(x=>!(x is string)))throw new InvalidOperationException("Expected native String[] metadata");return values.Cast<string>().ToArray();
    }
    static void ValidateNamedNames(string[] requested) {
        if(requested==null||requested.Length<1||requested.Length>64||requested.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>4096))throw new ArgumentException("Select 1..64 nonempty top-level field names.");
    }
    public static void ValidateNamedSelection(string[] requested,string[] current,string[] available) {
        ValidateNamedNames(requested);
        if(current==null||requested.Length!=current.Length)throw new ArgumentException("Resize named node rows explicitly before selection; requested field count must equal the current row count.");
        if(available==null||available.Length==0)throw new ArgumentException("Connect a typed Cluster input before selecting fields.");
        foreach(string name in requested)if(available.Count(x=>String.Equals(x,name,StringComparison.Ordinal))!=1)throw new ArgumentException("Unknown or ambiguous top-level Cluster field: "+name);
    }
    public static void VerifyNamedSelectionReadback(string[] requested,string[] actual) {
        if(actual==null||!requested.SequenceEqual(actual,StringComparer.Ordinal))throw new InvalidOperationException("NI named field selection readback differs from the requested ordered fields.");
    }
    public static Dictionary<string,object> PinAliases(IList<string> labels,IList<uint> terminals) {
        if(labels.Count!=terminals.Count)throw new ArgumentException("Pin metadata length mismatch");
        var counts=labels.Where(s=>!String.IsNullOrEmpty(s)).GroupBy(s=>s,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        var pins=new Dictionary<string,object>(StringComparer.Ordinal);
        for(int index=0;index<labels.Count;index++) {
            string label=labels[index];
            if(!String.IsNullOrEmpty(label)&&!label.Contains('.')&&!Regex.IsMatch(label,@"\A[0-9]+\z")&&counts[label]==1)pins[label]=terminals[index];
            pins[index.ToString(System.Globalization.CultureInfo.InvariantCulture)]=terminals[index];
        }
        return pins;
    }
    static Dictionary<string,object> FindControl(Dictionary<string,object> helpers,string target,string label) {
        var controls=InvokeHelper(helpers["Native_GetPanelControls"],new Dictionary<string,object>{{"target_path",target}},new[]{"control_ids"});
        var matches=new List<uint>();
        foreach(object raw in (IEnumerable)controls["control_ids"]) {
            uint id=Convert.ToUInt32(raw);
            var named=InvokeHelper(helpers["Native_GetControlLabel"],new Dictionary<string,object>{{"control_id",id}},new[]{"label"});
            if(String.Equals(Convert.ToString(named["label"]),label,StringComparison.Ordinal))matches.Add(id);
        }
        if(matches.Count!=1)throw new ArgumentException("find_control requires one exact front-panel label; found "+matches.Count+" for "+label);
        var terminal=InvokeHelper(helpers["Native_GetControlTerminal"],new Dictionary<string,object>{{"control_id",matches[0]}},new[]{"terminal_id"});
        return new Dictionary<string,object>{{"control_id",matches[0]},{"terminal_id",terminal["terminal_id"]}};
    }
    public static GeneratedTypeDependency ValidateTypePlacement(string path,bool global,string field) {
        var dependency=GeneratedTypeDependency.ValidateOne(path);ValidateTypePlacement(dependency,global,field);return dependency;
    }
    static void ValidateTypePlacement(GeneratedTypeDependency dependency,bool global,string field) {
        if(dependency.Kind!=(global?"global":"control"))throw new ArgumentException("Type artifact kind does not match the placement operation.");
        if(!global) {if(field!=null)throw new ArgumentException("CTL constants do not select global fields.");return;}
        if(String.IsNullOrWhiteSpace(field)||field.Length>4096)throw new ArgumentException("Global placement requires a bounded nonempty field label.");
        if(Data.Array(dependency.ReportData["controls"]).Select(Data.Map).Count(c=>String.Equals(Data.Text(c,"label"),field,StringComparison.Ordinal))!=1)throw new ArgumentException("Global field must match one exact label in the immutable type report.");
    }
    public static void CheckDependencyName(string path,IEnumerable<string> existing) {
        if(existing.Any(p=>!String.Equals(p,path,StringComparison.OrdinalIgnoreCase)&&String.Equals(Path.GetFileName(p),Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Dependencies have ambiguous identical filenames: "+path);
    }
    public static GeneratedTypeDependency RememberTypeDependency(string path,Dictionary<string,GeneratedViDependency> generated,Dictionary<string,GeneratedTypeDependency> types) {
        // Preserve the first file/report snapshot throughout the transaction;
        // subsequent placement or callee discovery must never replace it.
        GeneratedTypeDependency dependency;
        if(types.TryGetValue(path,out dependency)){dependency.VerifyUnchanged();return dependency;}
        if(generated.ContainsKey(path))throw new ArgumentException("Dependency cannot be both a generated program VI and a type artifact.");
        if(generated.Count+types.Count>=256)throw new ArgumentException("Recipe file dependencies exceed 256 paths.");
        dependency=GeneratedTypeDependency.ValidateOne(path);CheckDependencyName(dependency.Path,generated.Keys.Concat(types.Keys));
        types.Add(dependency.Path,dependency);return dependency;
    }
    static void AuditGeneratedDependency(object app,string path,List<object> refs,Dictionary<string,GeneratedViDependency> generated,Dictionary<string,GeneratedTypeDependency> types,HashSet<string> seen) {
        path=Path.GetFullPath(path);if(!seen.Add(path))return;
        if(seen.Count>256)throw new ArgumentException("SubVI dependency closure exceeds 256 paths");
        Storage.NoReparse(path);
        if(!NiDistribution.AcceptDependency(path)) {
            // Global VIs use .vi too. Classify them through their dedicated
            // report, then verify actual NI VIType and self-contained closure.
            // A malformed type report is an error, never a fallback to a VI.
            if(types.ContainsKey(path)||String.Equals(Path.GetExtension(path),".ctl",StringComparison.OrdinalIgnoreCase)||File.Exists(Path.Combine(Path.GetDirectoryName(path),"type-report.json"))) {
                var type=RememberTypeDependency(path,generated,types);TypedFilePlacement.VerifyNative(app,type,refs);return;
            }
            GeneratedViDependency dependency;
            if(generated.TryGetValue(path,out dependency))dependency.VerifyUnchanged();
            else dependency=GeneratedViDependency.ValidateOne(path);
            CheckDependencyName(path,generated.Keys.Concat(types.Keys));
            generated[path]=dependency;
        }
        object child=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(child);
        string actual=Path.GetFullPath(Convert.ToString(LabView.Invoke(child,"Path",BindingFlags.GetProperty)));
        if(!String.Equals(actual,path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Loaded subVI dependency path differs from its audited source: "+actual);
        if(!NiDistribution.AcceptDependency(path)&&Convert.ToInt32(LabView.Invoke(child,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Generated subVI is not in a compiled idle state: "+path);
        foreach(object callee in (IEnumerable)LabView.Invoke(child,"_CalleesPaths",BindingFlags.GetProperty)) {
            string next=Convert.ToString(callee);if(!String.IsNullOrWhiteSpace(next))AuditGeneratedDependency(app,next,refs,generated,types,seen);
        }
    }
    static void AuditNiDependencies(object app,object vi,List<object> refs,HashSet<string> visited) {
        foreach(object raw in (IEnumerable)LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)) {
            string path=Convert.ToString(raw);if(String.IsNullOrWhiteSpace(path))continue;
            path=Path.GetFullPath(path);if(!visited.Add(path))continue;
            if(!NiDistribution.AcceptDependency(path)&&!ApprovedOpenSource.Accept(path))throw new InvalidOperationException("Helper dependency outside accepted NI/pinned open source assets: "+path);
            // This also walks through an LLB member's existing container and its
            // parents; the virtual member itself need not be a filesystem file.
            Storage.NoReparse(path);
            object child=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(child);
            string actual=Path.GetFullPath(Convert.ToString(LabView.Invoke(child,"Path",BindingFlags.GetProperty)));
            if(!String.Equals(actual,path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("LabVIEW resolved an NI dependency to a different loaded VI: "+actual+" (expected "+path+").");
            Storage.NoReparse(actual);AuditNiDependencies(app,child,refs,visited);
        }
    }
    static uint Resolve(string reference,Dictionary<string,Dictionary<string,object>> values) {
        string[] parts=reference.Substring(1).Split('.');object value=values[parts[0]];
        foreach(string part in parts.Skip(1)) {
            var map=value as Dictionary<string,object>;
            if(map!=null){if(!map.ContainsKey(part))throw new ArgumentException("Unknown output reference: "+reference);value=map[part];}
            else {int i;var list=value as IList;if(list==null||!Int32.TryParse(part,out i)||i<0||i>=list.Count)throw new ArgumentException("Invalid reference index: "+reference);value=list[i];}
        }
        if(!(value is uint||value is int||value is long||value is ushort))throw new ArgumentException("Reference does not resolve to an NI object: "+reference);
        uint result=Convert.ToUInt32(value);if(result==0)throw new ArgumentException("Null native reference: "+reference);return result;
    }
    static Dictionary<string,object> InvokeHelper(object helper,Dictionary<string,object> input,string[] output) {
        foreach(var field in input)LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,field.Key,field.Value);
        LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);
        var error=(IList)LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,"error out");
        if(Convert.ToBoolean(error[0]))throw new InvalidOperationException("NI helper error "+error[1]+": "+error[2]);
        var result=new Dictionary<string,object>();foreach(string field in output)result[field]=LabView.Invoke(helper,"GetControlValue",BindingFlags.InvokeMethod,field);return result;
    }
}
}

