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
// The aggregate helper duplicates BD selection (including wires) and associated
// FP controls. Originals are never opened by this transaction. Supported inputs
// use bounded immutable own provenance plus current native contents. Neither
// provenance nor a historical editing verb substitutes for actual object checks.
public static class ViMerge {
    const string HelperName="OwnMergeDiagram_75a8ff880a4e45db8adbb9b45fd37ec5";
    const string HelperHash="7052eee4ecbd20d87eeeab2563c86162d92d0596ab2489efba8b8e2891e81316";
    static readonly string[] HelperNames={"Native_GetPanelControls","Native_GetControlLabel","Native_SetControlLabel","Native_GetObjectInfo","Native_GetControlTerminal","Native_GetTerminalTypeDescriptor","Own20261012_GetControlIndicator","Own20261012_GetObjectIdentity","Native_GetRootDiagram","Native_GetDiagramObjects","Native_GetDiagramWires","Native_GetWireBroken","Own20261012_GetStructureInfo","Own20261012_GetArrayInfo","Own20261012_PublicGetObjectPosition","Own20261012_GetTerminalContext","Native_GetTerminalName","Own20261012_GetConnectorInfo"};
    static readonly string[] InspectionNames={"OwnMergePanelInventory","OwnMergeObjectOwner","OwnMergeWireTerms"};
    internal sealed class Request {
        internal string Name,Filename,Token,Folder,Target,Scratch,RequestPath;
        internal int Dx,Dy;
        internal GeneratedViDependency Source,TargetSource;
        internal string SourcePlan,TargetPlan,SourcePlanHash,TargetPlanHash;
        internal Certificate SourceCertificate,TargetCertificate;
    }
    internal sealed class Certificate {
        internal readonly SortedDictionary<string,string> Files=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal string RootPlan;
        internal string Digest {get{return Data.Json.Serialize(Files);}}
        internal void Add(string path){Storage.NoReparse(path);if(!File.Exists(path)||new FileInfo(path).Length>268435456)throw new InvalidDataException("Missing or oversized merge provenance: "+path);string hash=Data.Hash(path);string old;if(Files.TryGetValue(path,out old)&&old!=hash)throw new IOException("Merge certificate changed while captured.");Files[path]=hash;}
        internal void Verify(){foreach(var pair in Files){Storage.NoReparse(pair.Key);if(!File.Exists(pair.Key)||Data.Hash(pair.Key)!=pair.Value)throw new IOException("Merge certificate file changed: "+pair.Key);}}
    }
    internal interface Backend:IDisposable {
        string NiRoot {get;} string Version {get;}
        object Open(string path); string LoadedPath(object vi); int State(object vi);
        string[] Callees(object vi); void Save(object vi,string path);
        Dictionary<string,object>[] Panel(string path); object[] Diagrams(string path); object Connector(string path);
        string RenameControl(string path,string uid,string newLabel);
        int Merge(string source,string target,int dx,int dy);
        object HelperEvidence {get;}
    }
    sealed class NativeBackend:Backend {
        readonly List<object> refs=new List<object>();
        readonly Dictionary<string,object> helpers=new Dictionary<string,object>();
        readonly Dictionary<string,string> loaded=new Dictionary<string,string>();
        readonly HashSet<string> helperClosure=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object app; NiSessionLease lease;
        public string NiRoot {get;private set;}
        public string Version {get{return Convert.ToString(LabView.Invoke(app,"Version",BindingFlags.GetProperty));}}
        public object HelperEvidence {get{return new{merge_helper=HelperName,sha256=HelperHash,loaded_paths=loaded,helper_callees=helperClosure.OrderBy(x=>x).ToArray(),source="merge-helpers/provenance.json",cleanup_invoked=false,clipboard_used=false};}}
        public NativeBackend() {
            try {
                NiRoot=NiDistribution.LibraryRoot;
                var metadata=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"recipes","helper-provenance.json"),Encoding.UTF8)))["helpers"]);
                string mergePath=Path.Combine(Storage.Root,"merge-helpers",HelperName+".vi");
                Storage.NoReparse(mergePath);if(Data.Hash(mergePath)!=HelperHash)throw new IOException("Merge helper hash differs from accepted handoff.");
                foreach(string name in HelperNames){string path=Path.Combine(Storage.Root,"recipes","helpers",name+".vi");Storage.NoReparse(path);if(Data.Hash(path)!=Data.Text(Data.Map(metadata[name]),"sha256"))throw new IOException("Unverified merge inspection helper: "+name);}
                string inspectionManifest=Path.Combine(Storage.Root,"merge-helpers","inspection-provenance.json");Storage.NoReparse(inspectionManifest);
                var inspection=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(inspectionManifest,Encoding.UTF8)))["helpers"]);
                foreach(string name in InspectionNames){string path=Path.Combine(Storage.Root,"merge-helpers",name+".vi");Storage.NoReparse(path);var record=Data.Map(inspection[name]);if(!Object.Equals(record["native_abi_verified"],true)||Data.Hash(path)!=Data.Text(record,"sha256"))throw new IOException("Merge content inspection helper not accepted: "+name);}
                lease=new NiSessionLease();app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();
                foreach(string name in HelperNames.Concat(InspectionNames).Concat(new[]{HelperName})) {
                    string path=name==HelperName?mergePath:InspectionNames.Contains(name)?Path.Combine(Storage.Root,"merge-helpers",name+".vi"):Path.Combine(Storage.Root,"recipes","helpers",name+".vi");
                    object helper=Open(path);Identity(this,helper,path);loaded[name]=LoadedPath(helper);helpers[name]=helper;
                    if(State(helper)!=1)throw new InvalidOperationException("Merge helper is not compiled idle: "+name);
                    AuditHelper(helper,0);
                }
            }catch{Dispose();throw;}
        }
        void AuditHelper(object vi,int depth) {
            if(depth>64)throw new InvalidDataException("Merge helper NI closure too deep.");
            foreach(string value in Callees(vi)) {string path=ViImport.AllowedCallee(value,NiRoot);if(!helperClosure.Add(path))continue;if(helperClosure.Count>256)throw new InvalidDataException("Merge helper closure too large.");object child=Open(path);Identity(this,child,path);AuditHelper(child,depth+1);}
        }
        public object Open(string path){try{object vi=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,path,"",false,0);refs.Add(vi);return vi;}catch(Exception ex){throw new InvalidOperationException("Merge GetVIReference failed for "+path,ex);}}
        public string LoadedPath(object vi){return Convert.ToString(LabView.Invoke(vi,"Path",BindingFlags.GetProperty));}
        public int State(object vi){return Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty));}
        public string[] Callees(object vi){return Values(LabView.Invoke(vi,"_CalleesPaths",BindingFlags.GetProperty)).Select(Convert.ToString).ToArray();}
        public void Save(object vi,string path){LabView.Invoke(vi,"SaveInstrument",BindingFlags.InvokeMethod,path,false,false);}
        Dictionary<string,object> Call(string name,Dictionary<string,object> input,params string[] outputs) {
            object vi=helpers[name];foreach(var field in input)LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,field.Key,field.Value);
            LabView.Invoke(vi,"Run",BindingFlags.InvokeMethod,false);
            var error=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,"error out") as IList;
            if(error==null||error.Count!=3||Convert.ToBoolean(error[0]))throw new InvalidOperationException("Merge inspection/helper error: "+(error==null?"missing error cluster":Convert.ToString(error[1])+": "+error[2]));
            var result=new Dictionary<string,object>();foreach(string field in outputs)result[field]=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,field);return result;
        }
        string Label(uint id){return Convert.ToString(Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",id}},"label")["label"]);}
        uint ControlTerminal(uint id){return Convert.ToUInt32(Call("Native_GetControlTerminal",new Dictionary<string,object>{{"control_id",id}},"terminal_id")["terminal_id"]);}
        object ControlState(string path,uint id){uint term=ControlTerminal(id);var info=Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name","bounds");return new{uid=Uid(path,id),label=Label(id),class_name=info["class_name"],bounds=Values(info["bounds"]),position=Position(id),terminal_uid=Uid(path,term),terminal_geometry=ObjectRow(path,term),descriptor=Values(Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",term}},"type_string")["type_string"]),indicator=Call("Own20261012_GetControlIndicator",new Dictionary<string,object>{{"control_id",id}},"is_indicator")["is_indicator"]};}
        public string RenameControl(string path,string uid,string newLabel){uint[] ids=Values(Call("Native_GetPanelControls",new Dictionary<string,object>{{"target_path",path}},"control_ids")["control_ids"]).Select(Convert.ToUInt32).Where(candidateId=>Uid(path,candidateId)==uid).ToArray();if(ids.Length!=1)throw new InvalidDataException("Rename requires exactly one actual control UID.");uint id=ids[0];string terminalUid=Uid(path,ControlTerminal(id));Call("Native_SetControlLabel",new Dictionary<string,object>{{"control_id",id},{"label",newLabel}});if(Label(id)!=newLabel||Uid(path,id)!=uid||Uid(path,ControlTerminal(id))!=terminalUid)throw new InvalidOperationException("UID-addressed label rename changed control/terminal identity.");return terminalUid;}
        object[] ControlValues(string path,object vi,uint id,string label,bool ambiguous,HashSet<string> labels){if(!ambiguous)return new[]{LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,label),LabView.Invoke(vi,"_GetCtrlDefaultValVariant",BindingFlags.InvokeMethod,label)};string before=Data.Json.Serialize(ControlState(path,id)),temp="OwnRead_"+Uid(path,id)+"_"+Guid.NewGuid().ToString("N").Substring(0,8);if(labels.Contains(temp))throw new InvalidOperationException("Unexpected scratch read-name collision.");object current,saved;try{Call("Native_SetControlLabel",new Dictionary<string,object>{{"control_id",id},{"label",temp}});if(Label(id)!=temp)throw new InvalidOperationException("Temporary scratch label readback differs.");current=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,temp);saved=LabView.Invoke(vi,"_GetCtrlDefaultValVariant",BindingFlags.InvokeMethod,temp);}finally{Call("Native_SetControlLabel",new Dictionary<string,object>{{"control_id",id},{"label",label}});}if(Data.Json.Serialize(ControlState(path,id))!=before)throw new InvalidOperationException("Temporary UID-addressed scratch read did not restore exact label/type/UID/geometry.");return new[]{current,saved};}
        public Dictionary<string,object>[] Panel(string path) {
            var rows=new List<Dictionary<string,object>>();
            object panelVi=Open(path);Identity(this,panelVi,path);
            var inventory=Call("OwnMergePanelInventory",new Dictionary<string,object>{{"target_path",path}},"all_object_ids","decoration_ids","pane_ids");
            object[] controls=Values(Call("Native_GetPanelControls",new Dictionary<string,object>{{"target_path",path}},"control_ids")["control_ids"]);
            string[] panelLabels=controls.Select(raw=>Label(Convert.ToUInt32(raw))).ToArray();var labelSet=new HashSet<string>(panelLabels,StringComparer.Ordinal);
            if(Values(inventory["pane_ids"]).Length!=1||Values(inventory["decoration_ids"]).Length!=0||!Values(inventory["all_object_ids"]).Select(Convert.ToUInt32).OrderBy(x=>x).SequenceEqual(controls.Select(Convert.ToUInt32).OrderBy(x=>x)))throw new ArgumentException("Actual FP inventory contains unsupported non-control objects or multiple panes; merge refuses to omit them.");
            foreach(object raw in controls) {
                uint id=Convert.ToUInt32(raw);
                string label=Convert.ToString(Call("Native_GetControlLabel",new Dictionary<string,object>{{"control_id",id}},"label")["label"]);
                var info=Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name","bounds");string kind=Convert.ToString(info["class_name"]);
                var identity=Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");
                if(!SamePath(Convert.ToString(identity["vi_path"]),path))throw new InvalidOperationException("Panel control belongs to a different VI.");
                object direction=Call("Own20261012_GetControlIndicator",new Dictionary<string,object>{{"control_id",id}},"is_indicator")["is_indicator"];
                uint terminal=Convert.ToUInt32(Call("Native_GetControlTerminal",new Dictionary<string,object>{{"control_id",id}},"terminal_id")["terminal_id"]);
                object[] descriptor=Values(Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",terminal}},"type_string")["type_string"]);
                int rank=0;string elementClass=null;
                if(kind=="Array") {
                    var arrayInfo=Call("Own20261012_GetArrayInfo",new Dictionary<string,object>{{"array_id",id}},"dimensions","element_id","terminal_id");rank=Convert.ToInt32(arrayInfo["dimensions"]);
                    elementClass=Convert.ToString(Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",arrayInfo["element_id"]}},"class_name")["class_name"]);
                    var arraySchema=Data.Map(DescriptorSchema(descriptor.Select(Convert.ToInt32).ToArray()));
                    if(Convert.ToInt32(arraySchema["type_code"])!=0x40||Convert.ToInt32(arraySchema["rank"])!=rank)throw new InvalidDataException("Array native descriptor rank differs from actual control metadata.");
                    if(elementClass=="Cluster"&&Convert.ToInt32(Data.Map(arraySchema["element"])["type_code"])!=0x50)throw new InvalidDataException("Cluster Array element schema differs from actual native class.");
                }
                if(kind=="Cluster"&&(descriptor.Length<3||(Convert.ToInt32(descriptor[1])&255)!=0x50))throw new InvalidDataException("Cluster descriptor does not match the actual top-level class.");
                object[] actualValues=ControlValues(path,panelVi,id,label,String.IsNullOrWhiteSpace(label)||panelLabels.Count(x=>x==label)!=1,labelSet);object rawValue=actualValues[0];
                int[] nativeDescriptor=descriptor.Select(Convert.ToInt32).ToArray();object schema=DescriptorSchema(nativeDescriptor);
                object value=CaptureDescriptorValue(rawValue,nativeDescriptor);
                object rawDefault=actualValues[1];
                rows.Add(new Dictionary<string,object>{{"label",label},{"class",kind},{"indicator",direction},{"uid",Convert.ToString(identity["uid"])},{"actual_loaded_path",path},{"descriptor",descriptor},{"metadata_genericSchema",schema},{"bounds",Values(info["bounds"])},{"position",Position(id)},{"value",value},{"value_clr_type",rawValue.GetType().FullName},{"default_value",CaptureDescriptorValue(rawDefault,nativeDescriptor)},{"default_clr_type",rawDefault.GetType().FullName},{"array_rank",rank},{"array_element_class",elementClass}});
                if(rows.Count>256)throw new InvalidDataException("Merge panel exceeds256 controls.");
            }
            return rows.ToArray();
        }
        public object[] Diagrams(string path) {
            var rows=new List<object>();var seen=new HashSet<uint>();
            uint root=Convert.ToUInt32(Call("Native_GetRootDiagram",new Dictionary<string,object>{{"target_path",path}},"diagram_id")["diagram_id"]);
            Visit(path,root,0,"",0,rows,seen);return rows.ToArray();
        }
        string Uid(string path,uint id) {if(id==0)throw new InvalidDataException("Zero native reference in merge snapshot.");var identity=Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");if(!SamePath(path,Convert.ToString(identity["vi_path"])))throw new InvalidOperationException("Merge snapshot object belongs to a different actual VI.");return Convert.ToString(identity["uid"]);}
        int[] Position(uint id){return Recipe.NativeObjectPosition(Call("Own20261012_PublicGetObjectPosition",new Dictionary<string,object>{{"object_id",id}},"position")["position"]);}
        Dictionary<string,object> ObjectRow(string path,uint id) {var info=Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name","bounds");return new Dictionary<string,object>{{"uid",Uid(path,id)},{"class",Convert.ToString(info["class_name"])},{"position",Position(id)},{"bounds",Values(info["bounds"])}};}
        Dictionary<string,object> TerminalRow(string path,uint id) {
            uint owner=Convert.ToUInt32(Call("OwnMergeObjectOwner",new Dictionary<string,object>{{"object_id",id}},"owner_id")["owner_id"]);
            var context=Call("Own20261012_GetTerminalContext",new Dictionary<string,object>{{"terminal_id",id}},"is_source","diagram_id");
            return new Dictionary<string,object>{{"uid",Uid(path,id)},{"owner",ObjectRow(path,owner)},{"diagram_uid",Uid(path,Convert.ToUInt32(context["diagram_id"]))},{"is_source",context["is_source"]},{"name",Convert.ToString(Call("Native_GetTerminalName",new Dictionary<string,object>{{"terminal_id",id}},"terminal_name")["terminal_name"])},{"descriptor",Values(Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",id}},"type_string")["type_string"])}};
        }
        public object Connector(string path) {
            var result=Call("Own20261012_GetConnectorInfo",new Dictionary<string,object>{{"target_path",path}},"pattern","terminal_count","control_ids");
            result["control_uids"]=Values(result["control_ids"]).Select(x=>Convert.ToUInt32(x)==0?"":Uid(path,Convert.ToUInt32(x))).ToArray();result.Remove("control_ids");return result;
        }
        void Visit(string path,uint diagram,int depth,string parent,int frame,List<object> rows,HashSet<uint> seen) {
            if(depth>16||!seen.Add(diagram)||seen.Count>256)throw new InvalidDataException("Merge diagram nesting/cycle exceeds supported boundary.");
            string diagramUid=Uid(path,diagram);var objects=new List<object>();var children=new List<Tuple<uint,string,int>>();
            foreach(object raw in Values(Call("Native_GetDiagramObjects",new Dictionary<string,object>{{"diagram_id",diagram}},"object_ids")["object_ids"])) {
                uint id=Convert.ToUInt32(raw);var obj=ObjectRow(path,id);string kind=Data.Text(obj,"class");objects.Add(obj);
                if(new[]{"ForLoop","WhileLoop","CaseStructure","EventStructure"}.Contains(kind)){int ordinal=0;foreach(object child in Values(Call("Own20261012_GetStructureInfo",new Dictionary<string,object>{{"structure_id",id}},"diagram_ids")["diagram_ids"]))children.Add(Tuple.Create(Convert.ToUInt32(child),Data.Text(obj,"uid"),ordinal++));}
                else if(kind.IndexOf("Structure",StringComparison.OrdinalIgnoreCase)>=0||kind.IndexOf("Loop",StringComparison.OrdinalIgnoreCase)>=0)throw new ArgumentException("Unsupported merge structure: "+kind);
                if(kind=="CallLibraryFunction"||kind=="CallLibraryFunctionNode"||kind=="CodeInterfaceNode"||kind=="AutomationRefnum")throw new ArgumentException("Merge cannot certify native/DLL/automation object: "+kind);
                if(objects.Count>4096)throw new InvalidDataException("Merge diagram object limit exceeded.");
            }
            object[] wires=Values(Call("Native_GetDiagramWires",new Dictionary<string,object>{{"diagram_id",diagram}},"wire_ids")["wire_ids"]);
            if(wires.Length>4096)throw new InvalidDataException("Merge wire limit exceeded.");
            var wireRows=new List<object>();
            foreach(object raw in wires){uint id=Convert.ToUInt32(raw);if(Convert.ToBoolean(Call("Native_GetWireBroken",new Dictionary<string,object>{{"wire_id",id}},"is_broken")["is_broken"]))throw new InvalidOperationException("Merge copy contains a broken wire.");var terms=Values(Call("OwnMergeWireTerms",new Dictionary<string,object>{{"wire_id",id}},"terminal_ids")["terminal_ids"]);if(terms.Length<2||terms.Length>512)throw new InvalidDataException("Bounded connected wire endpoints required.");wireRows.Add(new Dictionary<string,object>{{"uid",Uid(path,id)},{"terminals",terms.Select(t=>(object)TerminalRow(path,Convert.ToUInt32(t))).ToArray()}});}
            rows.Add(new Dictionary<string,object>{{"depth",depth},{"uid",diagramUid},{"parent_uid",parent},{"frame",frame},{"classes",objects.Select(Data.Map).Select(o=>Data.Text(o,"class")).OrderBy(x=>x,StringComparer.Ordinal).ToArray()},{"wire_count",wires.Length},{"objects",objects.OrderBy(o=>Data.Text(Data.Map(o),"uid"),StringComparer.Ordinal).ToArray()},{"wires",wireRows.OrderBy(o=>Data.Text(Data.Map(o),"uid"),StringComparer.Ordinal).ToArray()}});
            foreach(var child in children)Visit(path,child.Item1,depth+1,child.Item2,child.Item3,rows,seen);
        }
        public int Merge(string source,string target,int dx,int dy) {return Values(Call(HelperName,new Dictionary<string,object>{{"source_path",source},{"target_path",target},{"delta",new object[]{dx,dy}}},"selected_source_objects")["selected_source_objects"]).Length;}
        public void Dispose(){try{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);refs.Clear();LabView.Release(app);app=null;}finally{if(lease!=null){lease.Dispose();lease=null;}}}
    }
    static object[] Values(object value){var sequence=value as IEnumerable;if(sequence==null||value is string)throw new InvalidDataException("Native array metadata required.");return sequence.Cast<object>().ToArray();}
    // Parse the native TD before interpreting a COM tuple. A complex tuple,
    // Cluster and array can share object[] but have different native schemas.
    public static object DescriptorSchema(int[] descriptor){int nodes=0;return ReadSchema(descriptor,0,ref nodes);}
    static Dictionary<string,object> ReadSchema(int[] descriptor,int depth,ref int nodes) {
        if(depth>8||++nodes>256||descriptor==null||descriptor.Length<2||descriptor.Length>16384)throw new InvalidDataException("Bounded native type descriptor required.");
        int bytes=descriptor[0]&65535;if(bytes<4||(bytes&1)!=0||bytes/2!=descriptor.Length)throw new InvalidDataException("Truncated or inconsistent native descriptor length.");
        int code=descriptor[1]&255;var result=new Dictionary<string,object>{{"type_code",code},{"native_descriptor",descriptor}};
        if(code>=1&&code<=14||code==0x5F||code==0x21||code==0x30){result["kind"]=code>=12&&code<=14?"complex":code==0x21?"boolean":code==0x30?"string":"numeric";return result;}
        // Local NI Enum1 evidence proves0x16 plus actual UInt16 live/default.
        // Retain the native item/attribute payload verbatim; decoding its text
        // encoding is unnecessary for exact-copy equality and is not guessed.
        if(code==0x16){if(descriptor.Length<4||descriptor[2]<1||descriptor[2]>256)throw new InvalidDataException("Observed U16 Enum requires1..256 native items and a complete payload.");result["kind"]="enum";result["storage_clr_type"]=typeof(ushort).FullName;result["item_count"]=descriptor[2];result["enum_and_attribute_payload_words"]=descriptor.Skip(3).ToArray();return result;}
        if(code==0x50) {
            if(descriptor.Length<3||descriptor[2]<1||descriptor[2]>32)throw new InvalidDataException("Cluster descriptor requires1..32 fields.");
            int cursor=3;var members=new List<object>();for(int i=0;i<descriptor[2];i++)members.Add(ReadSchema(ChildDescriptor(descriptor,ref cursor),depth+1,ref nodes));result["kind"]="cluster";result["members"]=members.ToArray();return result;
        }
        if(code==0x40) {
            if(descriptor.Length<3||descriptor[2]<1||descriptor[2]>2)throw new InvalidDataException("Array descriptor requires rank1/2.");
            int rank=descriptor[2],cursor=3+2*rank;result["kind"]="array";result["rank"]=rank;result["element"]=ReadSchema(ChildDescriptor(descriptor,ref cursor),depth+1,ref nodes);return result;
        }
        throw new ArgumentException("Unsupported native FP value type code0x"+code.ToString("X2")+"; opaque/refnum/path/waveform handles are not interpreted as scalar data.");
    }
    static int[] ChildDescriptor(int[] parent,ref int cursor){if(cursor>=parent.Length)throw new InvalidDataException("Missing nested native descriptor.");int bytes=parent[cursor]&65535;if(bytes<4||(bytes&1)!=0||cursor+bytes/2>parent.Length)throw new InvalidDataException("Malformed nested native descriptor length.");int[] child=parent.Skip(cursor).Take(bytes/2).ToArray();cursor+=bytes/2;return child;}
    public static object CaptureDescriptorValue(object value,int[] descriptor){int budget=32768;return CaptureSchemaValue(value,Data.Map(DescriptorSchema(descriptor)),0,true,ref budget);}
    static object CaptureSchemaValue(object value,Dictionary<string,object> schema,int depth,bool top,ref int budget) {
        if(value==null||depth>8||--budget<0)throw new ArgumentException("Null, excessively nested or over-budget native FP value tree.");int code=Convert.ToInt32(schema["type_code"]);
        if(code==0x16){if(!(value is ushort)||(ushort)value>=Convert.ToInt32(schema["item_count"]))throw new ArgumentException("Observed U16 Enum TD requires its exact native UInt16 ordinal within the item table.");return new{kind="enum",native_type_code=code,clr_type=value.GetType().FullName,ordinal=(ushort)value};}
        if(code>=12&&code<=14) {
            var tuple=value as Array;if(tuple==null||tuple.Rank!=1||tuple.GetLowerBound(0)!=0||tuple.Length!=2)throw new ArgumentException("Native complex descriptor requires exactly two COM scalar components.");
            var components=tuple.Cast<object>().ToArray();if(components.Any(x=>!Numeric(x)))throw new ArgumentException("Complex components must be native finite numeric scalars.");
            if((budget-=components.Length)<0)throw new ArgumentException("Native value tree exceeds32768 nodes/scalars.");
            return new{kind="complex",native_type_code=code,clr_type=value.GetType().FullName,components=components.Select(ScalarEvidence).ToArray()};
        }
        if(code>=1&&code<=11||code==0x5F){if(!Numeric(value))throw new ArgumentException("Native numeric descriptor does not match the COM scalar value.");ScalarEvidence(value);return top?value:ScalarEvidence(value);}
        if(code==0x21){if(!(value is bool))throw new ArgumentException("Boolean TD does not match actual COM value.");return top?value:ScalarEvidence(value);}
        if(code==0x30){if(!(value is string)||((string)value).Length>4096)throw new ArgumentException("String TD requires bounded native text.");return top?value:ScalarEvidence(value);}
        var array=value as Array;if(array==null)throw new ArgumentException("Container TD requires an actual COM SAFEARRAY.");
        if(code==0x50) {
            object[] members=Values(schema["members"]);if(array.Rank!=1||array.GetLowerBound(0)!=0||array.GetType().GetElementType()!=typeof(object)||array.Length!=members.Length)throw new ArgumentException("Cluster TD member count/order differs from actual VARIANT tree.");
            var captured=new List<object>();for(int i=0;i<members.Length;i++)captured.Add(CaptureSchemaValue(array.GetValue(i),Data.Map(members[i]),depth+1,false,ref budget));
            return new{kind="cluster",clr_type=value.GetType().FullName,member_count=members.Length,members=captured.ToArray()};
        }
        if(code==0x40) {
            int rank=Convert.ToInt32(schema["rank"]);var element=Data.Map(schema["element"]);int elementCode=Convert.ToInt32(element["type_code"]);
            if(array.Rank!=rank||array.Length>4096)throw new ArgumentException("Native Array rank/count differs from descriptor boundary.");
            var shape=new int[rank];var lower=new int[rank];for(int i=0;i<rank;i++){shape[i]=array.GetLength(i);lower[i]=array.GetLowerBound(i);if(lower[i]!=0)throw new ArgumentException("Native Array lower bounds must be zero.");}
            if(elementCode==0x50){if(rank!=1||array.GetType().GetElementType()!=typeof(object))throw new ArgumentException("Observed rank1 Cluster-element Array requires an actual VARIANT SAFEARRAY and recursively matching Cluster items.");}
            else if(!(elementCode>=1&&elementCode<=11||elementCode==0x21||elementCode==0x30||elementCode==0x5F))throw new ArgumentException("Unobserved complex/nested Array element shape.");
            else {Type actualElement=array.GetType().GetElementType();if(elementCode==0x21?actualElement!=typeof(bool):elementCode==0x30?actualElement!=typeof(string):!new[]{typeof(byte),typeof(sbyte),typeof(short),typeof(ushort),typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double),typeof(decimal)}.Contains(actualElement))throw new ArgumentException("Native Array TD differs from its actual typed CLR SAFEARRAY element, including empty Arrays.");}
            if(array.Length>budget)throw new ArgumentException("Native value tree exceeds32768 nodes/scalars.");var elements=new List<object>();foreach(object item in array)elements.Add(CaptureSchemaValue(item,element,depth+1,false,ref budget));
            return new{kind="array",clr_type=value.GetType().FullName,rank,shape,lower_bounds=lower,element_schema=element,elements=elements.ToArray()};
        }
        throw new ArgumentException("Unsupported native value schema.");
    }
    // Never serialize a multidimensional SAFEARRAY directly: JSON serializers
    // can flatten it and make distinct shapes appear equal. Capture every axis,
    // CLR type and recursively ordered Cluster field explicitly instead.
    public static object CaptureBoundedValue(object value,string controlClass,int rank,string elementClass) {
        if(value==null)throw new ArgumentException("Merge control value cannot be null.");
        if(controlClass=="Array") {
            var array=value as Array;Type element=elementClass=="Digital"?typeof(double):elementClass=="Boolean"?typeof(bool):elementClass=="String"?typeof(string):null;
            if(array==null||element==null||array.GetType().GetElementType()!=element||array.Rank!=rank||rank<1||rank>2)throw new ArgumentException("Merge Arrays require a fresh typed rank1/2 DBL/Boolean/String SAFEARRAY; Cluster-element Arrays are unverified.");
            return CaptureArray(array,0);
        }
        if(controlClass=="Cluster")return CaptureCluster(value,0);
        if(controlClass=="Digital"||controlClass=="Slide") {if(!Numeric(value))throw new ArgumentException("Merge numeric controls require finite real scalars; complex values are outside this bounded contract.");ScalarEvidence(value);return value;}
        if(controlClass=="Boolean"&&value is bool)return value;
        if(controlClass=="String"&&value is string&&((string)value).Length<=4096)return value;
        throw new ArgumentException("Unsupported bounded merge control class/value: "+controlClass);
    }
    static bool Numeric(object value){return value is byte||value is sbyte||value is short||value is ushort||value is int||value is uint||value is long||value is ulong||value is float||value is double||value is decimal;}
    static object ScalarEvidence(object value) {
        if(value==null)throw new ArgumentException("Null Cluster member is unsupported.");
        if(Numeric(value)) {double number=Convert.ToDouble(value);if(Double.IsNaN(number)||Double.IsInfinity(number))throw new ArgumentException("Merge values must be finite.");}
        else if(!(value is bool)&&!(value is string))throw new ArgumentException("Merge Cluster members require finite numeric/Boolean/String scalars or nested Clusters.");
        if(value is string&&((string)value).Length>4096)throw new ArgumentException("Merge String member exceeds4096 characters.");
        return new Dictionary<string,object>{{"kind","scalar"},{"clr_type",value.GetType().FullName},{"value",value}};
    }
    static object CaptureCluster(object value,int depth) {
        var array=value as Array;
        if(depth>8||array==null||array.Rank!=1||array.GetType().GetElementType()!=typeof(object)||array.GetLowerBound(0)!=0||array.Length<1||array.Length>32)throw new ArgumentException("Merge Clusters require1..32 ordered VARIANT fields with at most8 nested Cluster levels.");
        var members=new List<object>();foreach(object member in array)members.Add(member is Array?CaptureCluster(member,depth+1):ScalarEvidence(member));
        return new Dictionary<string,object>{{"kind","cluster"},{"clr_type",value.GetType().FullName},{"member_count",array.Length},{"members",members.ToArray()}};
    }
    static object CaptureArray(Array value,int depth) {
        if(depth>8||value.Length>4096)throw new ArgumentException("Merge Array exceeds4096 elements.");
        var lengths=new int[value.Rank];var lower=new int[value.Rank];for(int axis=0;axis<value.Rank;axis++){lengths[axis]=value.GetLength(axis);lower[axis]=value.GetLowerBound(axis);if(lower[axis]!=0)throw new ArgumentException("Merge SAFEARRAY lower bounds must be zero.");}
        var elements=new List<object>();foreach(object element in value)elements.Add(ScalarEvidence(element));
        return new Dictionary<string,object>{{"kind","array"},{"clr_type",value.GetType().FullName},{"rank",value.Rank},{"shape",lengths},{"lower_bounds",lower},{"elements",elements.ToArray()}};
    }
    static bool SamePath(string a,string b){return String.Equals(ViImport.CanonicalLocalPath(a),ViImport.CanonicalLocalPath(b),StringComparison.OrdinalIgnoreCase);}
    static void Identity(Backend backend,object vi,string expected){if(!SamePath(backend.LoadedPath(vi),expected))throw new InvalidOperationException("Actual loaded VI Path differs from requested merge copy: "+backend.LoadedPath(vi));}
    static int Offset(Dictionary<string,object> args,string key){object value;if(!args.TryGetValue(key,out value))return 0;if(!(value is int||value is long)||Convert.ToInt64(value)<Int32.MinValue||Convert.ToInt64(value)>Int32.MaxValue)throw new ArgumentException(key+" requires an Int32 integer.");return Convert.ToInt32(value);}
    static Dictionary<string,object> ProvenanceJson(Certificate certificate,string path){certificate.Add(path);if(new FileInfo(path).Length>1048576)throw new InvalidDataException("Merge provenance JSON exceeds1 MiB.");return Data.Map(Data.Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8)));}
    static GeneratedViDependency EvidenceDependency(Dictionary<string,object> evidence) {
        var dependency=GeneratedViDependency.ValidateOne(Data.Text(evidence,"path"));
        if(dependency.Sha256!=Data.Text(evidence,"sha256")||dependency.ReportSha256!=Data.Text(evidence,"report_sha256")||!SamePath(dependency.ReportPath,Data.Text(evidence,"generation_report")))throw new IOException("Merge ancestor/report differs from recorded immutable evidence.");return dependency;
    }
    static Certificate Certify(GeneratedViDependency dependency) {var c=new Certificate();CertifyNode(dependency,c,new HashSet<string>(StringComparer.OrdinalIgnoreCase),new HashSet<string>(StringComparer.OrdinalIgnoreCase),0);c.Verify();return c;}
    static void CertifyNode(GeneratedViDependency dependency,Certificate certificate,HashSet<string> active,HashSet<string> done,int depth) {
        if(depth>16||done.Count>=64)throw new InvalidDataException("Merge ancestry exceeds16 levels/64 artifacts.");if(active.Contains(dependency.Path))throw new InvalidDataException("Merge ancestry cycle.");if(done.Contains(dependency.Path))return;
        active.Add(dependency.Path);certificate.Add(dependency.Path);certificate.Add(dependency.ReportPath);dependency.VerifyUnchanged();var report=dependency.ReportData;string folder=Path.GetDirectoryName(dependency.Path);
        if(report.ContainsKey("artifact_kind")) {
            string kind=Data.Text(report,"artifact_kind");if(kind!="imported_vi"&&kind!="merged_vi")throw new ArgumentException("Unsupported merge provenance root: "+kind);
            if(certificate.RootPlan==null)certificate.RootPlan=dependency.ReportPath;
            if(kind=="merged_vi") {foreach(string key in new[]{"source","target_source"}){if(!report.ContainsKey(key))throw new InvalidDataException("Merged snapshot missing ancestor evidence.");CertifyNode(EvidenceDependency(Data.Map(report[key])),certificate,active,done,depth+1);}}
            else {if(!report.ContainsKey("import_source"))throw new InvalidDataException("Import snapshot missing original source evidence.");var origin=Data.Map(report["import_source"]);if(!origin.ContainsKey("unchanged")||!Object.Equals(origin["unchanged"],true)||Data.Text(origin,"before_sha256")!=Data.Text(origin,"after_sha256")||!Regex.IsMatch(Data.Text(origin,"before_sha256"),"\\A[0-9a-f]{64}\\z"))throw new InvalidDataException("Import snapshot lacks unchanged source evidence.");}
        } else {
            string plan=Path.Combine(folder,dependency.ReportKind=="build"?"graph.json":"recipe.json");var doc=ProvenanceJson(certificate,plan);if(certificate.RootPlan==null)certificate.RootPlan=plan;
            if(dependency.ReportKind=="build")Graph.Parse(doc);
            else {
                if(!doc.ContainsKey("steps")||!report.ContainsKey("steps"))throw new InvalidDataException("Merge recipe plan/report missing steps.");
                object[] steps=Data.Array(doc["steps"]),logged=Data.Array(report["steps"]);if(steps.Length!=logged.Length||steps.Length>500)throw new ArgumentException("Merge authoring plan/report steps differ or exceed500.");
                string catalogPath=Path.Combine(Storage.Root,"recipes","operations.json");var catalog=ProvenanceJson(certificate,catalogPath);
                for(int i=0;i<steps.Length;i++){var step=Data.Map(steps[i]);var log=Data.Map(logged[i]);string op=Data.Text(step,"op");if(!catalog.ContainsKey(op)||op!=Data.Text(log,"operation")||Data.Text(step,"id")!=Data.Text(log,"id"))throw new ArgumentException("Unrecognized or mismatched product recipe operation: "+op);}
                string copy=Path.Combine(folder,"source-copy-report.json");
                if(doc.ContainsKey("source_vi")){var record=ProvenanceJson(certificate,copy);var parent=EvidenceDependency(Data.Map(record["source"]));if(!SamePath(parent.Path,Data.Text(doc,"source_vi"))||!SamePath(dependency.Path,Data.Text(record,"target_path"))||!Object.Equals(record["source_file_unchanged"],true))throw new InvalidDataException("Copy ancestry identifies a different source or target.");CertifyNode(parent,certificate,active,done,depth+1);}
                else if(File.Exists(copy))throw new ArgumentException("Unexpected source-copy evidence without recipe source_vi.");
            }
        }
        active.Remove(dependency.Path);done.Add(dependency.Path);
    }
    internal static Request Parse(Dictionary<string,object> args) {
        Data.Keys(args,"source_path","target_source_path","name","filename","dx","dy");string name=Data.Text(args,"name");
        if(!Regex.IsMatch(name,@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z")||Regex.IsMatch(name,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z",RegexOptions.IgnoreCase))throw new ArgumentException("Merge name must be a non-reserved ASCII identifier of at most40 characters.");
        var result=new Request{Name=name,Filename=args.ContainsKey("filename")?Storage.ArtifactFilename(args["filename"],".vi"):null,Dx=Offset(args,"dx"),Dy=Offset(args,"dy"),Source=GeneratedViDependency.ValidateOne(Data.Text(args,"source_path")),TargetSource=GeneratedViDependency.ValidateOne(Data.Text(args,"target_source_path"))};
        if(SamePath(result.Source.Path,result.TargetSource.Path))throw new ArgumentException("Merge source and target source must be two distinct VI artifacts.");
        result.SourceCertificate=Certify(result.Source);result.TargetCertificate=Certify(result.TargetSource);result.SourcePlan=result.SourceCertificate.RootPlan;result.TargetPlan=result.TargetCertificate.RootPlan;result.SourcePlanHash=Data.Hash(result.SourcePlan);result.TargetPlanHash=Data.Hash(result.TargetPlan);return result;
    }
    static void Unchanged(Request request){request.Source.VerifyUnchanged();request.TargetSource.VerifyUnchanged();request.SourceCertificate.Verify();request.TargetCertificate.Verify();if(Data.Hash(request.SourcePlan)!=request.SourcePlanHash||Data.Hash(request.TargetPlan)!=request.TargetPlanHash)throw new IOException("Merge authoring plan changed during transaction.");}
    static object Input(Request r){return new{source_path=r.Source.Path,target_source_path=r.TargetSource.Path,name=r.Name,filename=r.Filename??r.Name+".vi",dx=r.Dx,dy=r.Dy};}
    internal static Request Prepare(Request r) {
        r.Token=Guid.NewGuid().ToString("N");r.Folder=Path.Combine(Storage.Root,"projects",r.Name+"_Merge_"+r.Token);Storage.NoReparse(r.Folder);Directory.CreateDirectory(r.Folder);
        r.Target=Path.Combine(r.Folder,r.Filename??r.Name+".vi");r.Scratch=Path.Combine(r.Folder,"MergeSource_"+r.Token+".vi");
        if(SamePath(r.Target,r.Scratch))throw new ArgumentException("filename collides with source scratch copy.");
        r.RequestPath=Path.Combine(r.Folder,"merge-request.json");Storage.WriteNew(r.RequestPath,new{schema_version=2,token=r.Token,args=Input(r),source=r.Source.ToEvidence(),target_source=r.TargetSource.ToEvidence(),source_plan_sha256=r.SourcePlanHash,target_plan_sha256=r.TargetPlanHash,source_certificate=r.SourceCertificate.Files,target_certificate=r.TargetCertificate.Files});return r;
    }
    internal static Request ReadRequest(string path) {
        path=ViImport.CanonicalLocalPath(path);if(Path.GetFileName(path)!="merge-request.json"||new FileInfo(path).Length>1048576)throw new ArgumentException("Bounded merge-request.json required.");
        var doc=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8)));Data.Keys(doc,"schema_version","token","args","source","target_source","source_plan_sha256","target_plan_sha256","source_certificate","target_certificate");
        if(!Object.Equals(doc["schema_version"],2))throw new ArgumentException("Invalid merge request schema.");string token=Data.Text(doc,"token");if(!Regex.IsMatch(token,@"\A[0-9a-f]{32}\z"))throw new ArgumentException("Invalid merge token.");
        Request r=Parse(Data.Map(doc["args"]));r.Token=token;r.Folder=Path.Combine(Storage.Root,"projects",r.Name+"_Merge_"+token);r.Target=Path.Combine(r.Folder,r.Filename);r.Scratch=Path.Combine(r.Folder,"MergeSource_"+token+".vi");r.RequestPath=path;
        if(!SamePath(Path.GetDirectoryName(path),r.Folder)||SamePath(r.Target,r.Scratch))throw new UnauthorizedAccessException("Merge worker/output identity differs.");
        foreach(var pair in new[]{new{Actual=r.Source,Expected=Data.Map(doc["source"])},new{Actual=r.TargetSource,Expected=Data.Map(doc["target_source"])}})if(!SamePath(pair.Actual.Path,Data.Text(pair.Expected,"path"))||pair.Actual.Sha256!=Data.Text(pair.Expected,"sha256")||pair.Actual.ReportSha256!=Data.Text(pair.Expected,"report_sha256"))throw new IOException("Merge input snapshot changed before worker dispatch.");
        if(r.SourcePlanHash!=Data.Text(doc,"source_plan_sha256")||r.TargetPlanHash!=Data.Text(doc,"target_plan_sha256")||r.SourceCertificate.Digest!=Data.Json.Serialize(new SortedDictionary<string,string>(Data.Map(doc["source_certificate"]).ToDictionary(p=>p.Key,p=>Convert.ToString(p.Value)),StringComparer.OrdinalIgnoreCase))||r.TargetCertificate.Digest!=Data.Json.Serialize(new SortedDictionary<string,string>(Data.Map(doc["target_certificate"]).ToDictionary(p=>p.Key,p=>Convert.ToString(p.Value)),StringComparer.OrdinalIgnoreCase)))throw new IOException("Merge ancestry certificate snapshot changed.");return r;
    }
    public static object Merge(Dictionary<string,object> args) {
        Request r=Prepare(Parse(args));
        using(var locks=new Pins()) {
            locks.Add(r.RequestPath);Pin(r.Source,locks);Pin(r.TargetSource,locks);PinCertificate(r,locks);Unchanged(r);
            NiApplicationLifetime.EnsureForBuild();
            var start=new ProcessStartInfo(Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),"--merge-worker \""+r.RequestPath+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Storage.Root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            using(var child=new Process{StartInfo=start}) {child.Start();var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();if(!child.WaitForExit(180000)){try{child.Kill();child.WaitForExit(5000);}catch{}throw new TimeoutException("Merge worker timed out; partial copies retained: "+r.Folder);}if(!output.Wait(5000)||!error.Wait(5000))throw new IOException("Merge worker output pipes remained open.");if(child.ExitCode!=0)throw new InvalidOperationException("Merge failed; partial copies retained: "+r.Folder+". "+error.Result);}
            Unchanged(r);var report=GeneratedViDependency.ValidateOne(r.Target).ReportData;if(Data.Text(report,"artifact_kind")!="merged_vi")throw new IOException("Worker did not return an accepted merge artifact.");return report;
        }
    }
    sealed class Pins:IDisposable {readonly Dictionary<string,FileStream> files=new Dictionary<string,FileStream>(StringComparer.OrdinalIgnoreCase);internal void Add(string path){Storage.NoReparse(path);if(!files.ContainsKey(path))files[path]=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);}public void Dispose(){foreach(var file in files.Values)file.Dispose();files.Clear();}}
    static void Pin(GeneratedViDependency d,Pins pins){pins.Add(d.Path);pins.Add(d.ReportPath);d.VerifyUnchanged();}
    static void PinCertificate(Request r,Pins pins){foreach(string path in r.SourceCertificate.Files.Keys.Concat(r.TargetCertificate.Files.Keys))pins.Add(path);Unchanged(r);}
    public static object Worker(string requestPath){return RunRequest(requestPath,()=>new NativeBackend());}
    public static void ValidatePanelMerge(Dictionary<string,object>[] source,Dictionary<string,object>[] target,Dictionary<string,object>[] after) {
        ValidateLabels(source);ValidateLabels(target,true);if(source.Any(s=>target.Any(t=>LabelText(s)==LabelText(t))))throw new ArgumentException("Normalized source names must be disjoint from original target names.");
        if(after==null)return;ValidateLabels(after,true);if(after.Length!=source.Length+target.Length)throw new InvalidOperationException("Merge did not preserve every source and target FP control.");
        foreach(var before in source){var matches=after.Where(a=>LabelText(a)==LabelText(before)).ToArray();if(matches.Length!=1||Signature(matches[0])!=Signature(before))throw new InvalidOperationException("Copied normalized source FP control class/direction/type/value differs.");}
        foreach(var before in target){var row=after.SingleOrDefault(a=>Data.Text(a,"uid")==Data.Text(before,"uid"));if(row==null||LabelText(row)!=LabelText(before)||Signature(row)!=Signature(before)||Data.Json.Serialize(row["bounds"])!=Data.Json.Serialize(before["bounds"])||Data.Json.Serialize(row["position"])!=Data.Json.Serialize(before["position"]))throw new InvalidOperationException("Original target FP control label/UID/type/value/default/position/bounds changed.");}
        if(after.Select(a=>Data.Text(a,"uid")).Distinct(StringComparer.Ordinal).Count()!=after.Length)throw new InvalidOperationException("Copied FP controls are not physically one-to-one.");
    }
    static string Signature(Dictionary<string,object> row){return Data.Text(row,"class")+"|"+Convert.ToString(row["indicator"])+"|"+Data.Json.Serialize(row["descriptor"])+"|"+Data.Json.Serialize(row.ContainsKey("metadata_genericSchema")?row["metadata_genericSchema"]:null)+"|"+Data.Json.Serialize(row["value"])+"|"+Data.Text(row,"value_clr_type")+"|"+Data.Json.Serialize(row["default_value"])+"|"+Data.Text(row,"default_clr_type");}
    static string LabelText(Dictionary<string,object> row){object value;if(!row.TryGetValue("label",out value)||!(value is string)||((string)value).Length>4096)throw new ArgumentException("Bounded actual label text required.");return (string)value;}
    static void ValidateLabels(Dictionary<string,object>[] rows,bool allowAmbiguous=false) {
        if(rows==null||rows.Length>256)throw new ArgumentException("Merge requires bounded panel metadata.");var labels=new HashSet<string>(StringComparer.Ordinal);var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows){string label=LabelText(row),uid=Data.Text(row,"uid");if(!identities.Add(uid)||!allowAmbiguous&&(String.IsNullOrWhiteSpace(label)||!labels.Add(label)))throw new ArgumentException("Merge requires distinct physical UIDs and normalized source names.");Recipe.NativeObjectPosition(row["position"]);bool schemaPresent=row.ContainsKey("metadata_genericSchema");if(schemaPresent&&Data.Json.Serialize(DescriptorSchema(Values(row["descriptor"]).Select(Convert.ToInt32).ToArray()))!=Data.Json.Serialize(row["metadata_genericSchema"]))throw new ArgumentException("FP snapshot schema differs from the native descriptor.");if(!schemaPresent&&!new[]{"Digital","Slide","Boolean","String","Array","Cluster"}.Contains(Data.Text(row,"class"))||!(row["indicator"] is bool))throw new ArgumentException("FP snapshot requires an accepted native descriptor/value schema.");}
    }
    public static Dictionary<string,object>[] PlanSourceNames(Dictionary<string,object>[] source,Dictionary<string,object>[] target,string sourceName){ValidateLabels(source,true);ValidateLabels(target,true);if(String.IsNullOrWhiteSpace(sourceName))throw new ArgumentException("Source prefix required.");string prefix=Regex.Replace(sourceName,"[^A-Za-z0-9_]","_");prefix=prefix.Substring(0,Math.Min(24,prefix.Length))+"__";var used=new HashSet<string>(target.Select(LabelText),StringComparer.Ordinal);var counts=source.GroupBy(LabelText,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);foreach(var row in source)if(!String.IsNullOrWhiteSpace(LabelText(row))&&counts[LabelText(row)]==1&&!used.Contains(LabelText(row)))used.Add(LabelText(row));var result=new List<Dictionary<string,object>>();foreach(var row in source.OrderBy(r=>Data.Text(r,"uid"),StringComparer.Ordinal)){string old=LabelText(row),uid=Data.Text(row,"uid"),name=old;bool rename=String.IsNullOrWhiteSpace(old)||counts[old]>1||target.Any(t=>LabelText(t)==old);if(rename){string text=String.IsNullOrWhiteSpace(old)?"UID"+uid:old;text=text.Substring(0,Math.Min(3900,text.Length));string candidate=prefix+text;name=candidate;int suffix=0;while(!used.Add(name)){suffix++;if(suffix>512)throw new InvalidOperationException("Cannot allocate bounded unique source names.");name=candidate+"_UID"+uid+(suffix==1?"":"_"+suffix);} }result.Add(new Dictionary<string,object>{{"source_uid",uid},{"original_label",old},{"new_label",name},{"renamed",rename},{"source_prefix",prefix}});}return result.ToArray();}
    static object SchemaShape(Dictionary<string,object> schema){var result=new SortedDictionary<string,object>(StringComparer.Ordinal);foreach(var pair in schema){if(pair.Key=="native_descriptor"||pair.Key=="enum_and_attribute_payload_words")continue;if(pair.Key=="members")result[pair.Key]=Values(pair.Value).Select(x=>SchemaShape(Data.Map(x))).ToArray();else if(pair.Key=="element")result[pair.Key]=SchemaShape(Data.Map(pair.Value));else result[pair.Key]=pair.Value;}return result;}
    static string RenameValueSignature(Dictionary<string,object> row){object type=row.ContainsKey("metadata_genericSchema")?SchemaShape(Data.Map(row["metadata_genericSchema"])):row["descriptor"];return Data.Json.Serialize(new{class_name=row["class"],indicator=row["indicator"],type,actual_native_type_flags=Convert.ToInt32(Values(row["descriptor"])[1])&~0x4000,value=row["value"],value_clr_type=row["value_clr_type"],default_value=row["default_value"],default_clr_type=row["default_clr_type"]});}
    // The observed native root-name attribute is a padded Pascal byte suffix.
    // Compare every preceding byte, including nested field names and Enum item
    // payload. Accept an encoding only when its bytes exactly match the native
    // suffix; an unrecognized attribute layout fails closed.
    static byte[] DescriptorWithoutRootName(int[] td,string label){DescriptorSchema(td);byte[] bytes=td.Skip(1).SelectMany(w=>new[]{(byte)((w&65535)>>8),(byte)(w&255)}).ToArray();if(label.Length==0){if((td[1]&0x4000)!=0)throw new InvalidOperationException("Unobserved empty native name attribute layout.");return bytes;}if((td[1]&0x4000)==0)throw new InvalidOperationException("Native named TD lacks the observed0x4000 root-name attribute flag.");bytes[0]=(byte)(bytes[0]&0xBF);foreach(var encoding in new[]{Encoding.UTF8,Encoding.Default}){byte[] text=encoding.GetBytes(label);if(text.Length>255)continue;byte[] suffix=new byte[((text.Length+1)+1)/2*2];suffix[0]=(byte)text.Length;Array.Copy(text,0,suffix,1,text.Length);if(bytes.Length>=suffix.Length&&bytes.Skip(bytes.Length-suffix.Length).SequenceEqual(suffix))return bytes.Take(bytes.Length-suffix.Length).ToArray();}throw new InvalidOperationException("Renamed native TD lacks an exact bounded observed root-label suffix.");}
    static bool RenamedDescriptor(Dictionary<string,object> before,Dictionary<string,object> after){if(!before.ContainsKey("metadata_genericSchema"))return Data.Json.Serialize(before["descriptor"])==Data.Json.Serialize(after["descriptor"]);return DescriptorWithoutRootName(Values(before["descriptor"]).Select(Convert.ToInt32).ToArray(),LabelText(before)).SequenceEqual(DescriptorWithoutRootName(Values(after["descriptor"]).Select(Convert.ToInt32).ToArray(),LabelText(after)));}
    public static void ValidateSourceRenaming(Dictionary<string,object>[] before,Dictionary<string,object>[] after,Dictionary<string,object>[] mapping){ValidateLabels(before,true);ValidateLabels(after);if(before.Length!=after.Length||mapping.Length!=before.Length)throw new InvalidOperationException("Source rename inventory differs.");foreach(var old in before){string uid=Data.Text(old,"uid");var names=mapping.SingleOrDefault(m=>Data.Text(m,"source_uid")==uid);var row=after.SingleOrDefault(r=>Data.Text(r,"uid")==uid);if(names==null||row==null||Convert.ToString(names["original_label"])!=LabelText(old)||Convert.ToString(names["new_label"])!=LabelText(row)||RenameValueSignature(old)!=RenameValueSignature(row)||Data.Json.Serialize(old["position"])!=Data.Json.Serialize(row["position"])||Convert.ToBoolean(names["renamed"])&&!RenamedDescriptor(old,row))throw new InvalidOperationException("UID-addressed source rename changed typed content/default/native payload or anchor.");if(!Convert.ToBoolean(names["renamed"])&&(Signature(old)!=Signature(row)||Data.Json.Serialize(old["bounds"])!=Data.Json.Serialize(row["bounds"])))throw new InvalidOperationException("Unrenamed source control metadata/geometry changed.");}}
    internal static void ValidateDiagrams(object[] source,object[] target,object[] after) {
        if(source.Length==0||target.Length==0||after.Length!=source.Length+target.Length-1)throw new InvalidOperationException("Merged diagram count differs from source+target.");
        var a=Data.Map(source[0]);var b=Data.Map(target[0]);var c=Data.Map(after[0]);
        string[] expected=Values(a["classes"]).Concat(Values(b["classes"])).Select(Convert.ToString).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        if(!expected.SequenceEqual(Values(c["classes"]).Select(Convert.ToString).OrderBy(x=>x,StringComparer.Ordinal))||Convert.ToInt32(c["wire_count"])!=Convert.ToInt32(a["wire_count"])+Convert.ToInt32(b["wire_count"]))throw new InvalidOperationException("Merged root classes/wires differ from aggregate inputs.");
        Func<object,string> counts=x=>{var row=Data.Map(x);return Data.Json.Serialize(new{depth=row["depth"],classes=row["classes"],wire_count=row["wire_count"]});};
        string[] nested=source.Skip(1).Concat(target.Skip(1)).Select(counts).OrderBy(x=>x,StringComparer.Ordinal).ToArray();if(!nested.SequenceEqual(after.Skip(1).Select(counts).OrderBy(x=>x,StringComparer.Ordinal)))throw new InvalidOperationException("Merged nested classes/wires differ from inputs.");
    }
    static Dictionary<string,object>[] Rows(Dictionary<string,object> row,string key){return Values(row[key]).Select(Data.Map).ToArray();}
    static bool Geometry(Dictionary<string,object> before,Dictionary<string,object> after,int dx,int dy){int[] a=Recipe.NativeObjectPosition(before["position"]),b=Recipe.NativeObjectPosition(after["position"]);return Data.Text(before,"class")==Data.Text(after,"class")&&(long)a[0]+dx==b[0]&&(long)a[1]+dy==b[1]&&Data.Json.Serialize(before["bounds"])==Data.Json.Serialize(after["bounds"]);}
    static bool DiagramOwner(Dictionary<string,object> owner){string kind=Data.Text(owner,"class");return kind=="Diagram"||kind=="TopLevelDiagram";}
    static string WireExact(Dictionary<string,object> wire){return Data.Json.Serialize(Rows(wire,"terminals").Select(t=>{var copy=new Dictionary<string,object>(t);var owner=Data.Map(t["owner"]);if(DiagramOwner(owner))copy["owner"]=new{uid=owner["uid"],@class=owner["class"]};return copy;}).OrderBy(t=>Data.Text(t,"uid"),StringComparer.Ordinal).ToArray());}
    static bool CaptionObject(Dictionary<string,object> row){string kind=Data.Text(row,"class");return kind=="ControlTerminal"||kind=="LocalVariable";}
    internal static void ValidateRenameContent(object[] before,object[] after,Dictionary<string,object>[] mapping){if(before.Length!=after.Length)throw new InvalidOperationException("Renaming changed Diagram inventory.");foreach(var raw in before){var d=Data.Map(raw);var candidates=after.Select(Data.Map).Where(a=>Data.Text(a,"uid")==Data.Text(d,"uid")).ToArray();if(candidates.Length!=1)throw new InvalidOperationException("Renaming changed Diagram identity.");var z=candidates[0];foreach(string key in new[]{"depth","parent_uid","frame","classes","wire_count"})if(Data.Json.Serialize(d[key])!=Data.Json.Serialize(z[key]))throw new InvalidOperationException("Renaming changed native containment/class/wire counts.");var oldObjects=Rows(d,"objects");var objects=Rows(z,"objects");if(oldObjects.Length!=objects.Length)throw new InvalidOperationException("Renaming changed object inventory.");foreach(var obj in oldObjects){var a=objects.SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(obj,"uid"));if(a==null||Data.Text(a,"class")!=Data.Text(obj,"class")||Data.Json.Serialize(a["position"])!=Data.Json.Serialize(obj["position"])||!CaptionObject(obj)&&Data.Json.Serialize(a["bounds"])!=Data.Json.Serialize(obj["bounds"]))throw new InvalidOperationException("Label rename changed original source BD identity/anchor or unrelated geometry.");}foreach(var wire in Rows(d,"wires")){var a=Rows(z,"wires").SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(wire,"uid"));if(a==null)throw new InvalidOperationException("Label rename replaced a source wire.");var terminals=Rows(a,"terminals");foreach(var terminal in Rows(wire,"terminals")){var t=terminals.SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(terminal,"uid"));if(t==null||terminals.Length!=Rows(wire,"terminals").Length||Data.Text(t,"diagram_uid")!=Data.Text(terminal,"diagram_uid")||!Object.Equals(t["is_source"],terminal["is_source"]))throw new InvalidOperationException("Label rename changed source wire endpoint identity/owner/direction.");var oldOwner=Data.Map(terminal["owner"]);var newOwner=Data.Map(t["owner"]);if(Data.Text(oldOwner,"uid")!=Data.Text(newOwner,"uid")||Data.Text(oldOwner,"class")!=Data.Text(newOwner,"class")||!DiagramOwner(oldOwner)&&(Data.Json.Serialize(oldOwner["position"])!=Data.Json.Serialize(newOwner["position"])||!CaptionObject(oldOwner)&&Data.Json.Serialize(oldOwner["bounds"])!=Data.Json.Serialize(newOwner["bounds"])))throw new InvalidOperationException("Label rename changed endpoint ownership.");var oldTd=Values(terminal["descriptor"]).Select(Convert.ToInt32).ToArray();var newTd=Values(t["descriptor"]).Select(Convert.ToInt32).ToArray();if((oldTd[1]&~0x4000)!=(newTd[1]&~0x4000)||Data.Json.Serialize(SchemaShape(Data.Map(DescriptorSchema(oldTd))))!=Data.Json.Serialize(SchemaShape(Data.Map(DescriptorSchema(newTd)))))throw new InvalidOperationException("Label rename changed source endpoint native type shape.");string oldName=Convert.ToString(terminal["name"]),newName=Convert.ToString(t["name"]);if(Data.Json.Serialize(oldTd)!=Data.Json.Serialize(newTd)&&!DescriptorWithoutRootName(oldTd,oldName).SequenceEqual(DescriptorWithoutRootName(newTd,newName)))throw new InvalidOperationException("Label rename changed native source endpoint payload beyond its exact root-name suffix.");if(oldName!=newName&&!mapping.Any(m=>Convert.ToString(m["original_label"])==oldName&&Convert.ToString(m["new_label"])==newName))throw new InvalidOperationException("Label rename changed unrelated source terminal name.");}}}}
    internal static Dictionary<string,string> ValidateContent(object[] source,object[] target,object[] after,int dx,int dy) {
        ValidateDiagrams(source,target,after);var s=source.Select(Data.Map).ToArray();var t=target.Select(Data.Map).ToArray();var a=after.Select(Data.Map).ToArray();
        var originalObjects=new HashSet<string>(t.SelectMany(d=>Rows(d,"objects")).Select(o=>Data.Text(o,"uid")),StringComparer.Ordinal);
        var originalWires=new HashSet<string>(t.SelectMany(d=>Rows(d,"wires")).Select(o=>Data.Text(o,"uid")),StringComparer.Ordinal);
        var used=new HashSet<string>(StringComparer.Ordinal);var usedWires=new HashSet<string>(StringComparer.Ordinal);var map=new Dictionary<string,string>(StringComparer.Ordinal);var diagrams=new Dictionary<string,string>(StringComparer.Ordinal);var terminals=new Dictionary<string,string>(StringComparer.Ordinal);var terminalInverse=new Dictionary<string,string>(StringComparer.Ordinal);
        if(a.Select(d=>Data.Text(d,"uid")).Distinct().Count()!=a.Length||a.SelectMany(d=>Rows(d,"objects")).Select(o=>Data.Text(o,"uid")).Distinct().Count()!=a.Sum(d=>Rows(d,"objects").Length)||a.SelectMany(d=>Rows(d,"wires")).Select(o=>Data.Text(o,"uid")).Distinct().Count()!=a.Sum(d=>Rows(d,"wires").Length))throw new InvalidOperationException("Duplicate physical object identities in native content snapshot.");
        foreach(var d in t){var actual=a.SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(d,"uid"));if(actual==null||Convert.ToString(actual["parent_uid"])!=Convert.ToString(d["parent_uid"])||Convert.ToInt32(actual["frame"])!=Convert.ToInt32(d["frame"]))throw new InvalidOperationException("Original target Diagram identity/containment changed.");foreach(var obj in Rows(d,"objects")){var keep=Rows(actual,"objects").SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(obj,"uid"));if(keep==null||!Geometry(obj,keep,0,0))throw new InvalidOperationException("Original target BD object UID/position/bounds changed.");}foreach(var wire in Rows(d,"wires")){var keep=Rows(actual,"wires").SingleOrDefault(x=>Data.Text(x,"uid")==Data.Text(wire,"uid"));if(keep==null||WireExact(wire)!=WireExact(keep))throw new InvalidOperationException("Original target wire UID/endpoints/type/direction changed.");}}
        foreach(var d in s){bool root=Convert.ToInt32(d["depth"])==0;Dictionary<string,object> actual;if(root)actual=a[0];else {string owner;if(!map.TryGetValue(Data.Text(d,"parent_uid"),out owner))throw new InvalidOperationException("Source structure has no one-to-one copied owner.");actual=a.SingleOrDefault(x=>Convert.ToString(x["parent_uid"])==owner&&Convert.ToInt32(x["frame"])==Convert.ToInt32(d["frame"]));if(actual==null)throw new InvalidOperationException("Source nested Diagram/frame disappeared.");}diagrams.Add(Data.Text(d,"uid"),Data.Text(actual,"uid"));foreach(var obj in Rows(d,"objects")){var candidates=Rows(actual,"objects").Where(x=>!originalObjects.Contains(Data.Text(x,"uid"))&&!used.Contains(Data.Text(x,"uid"))&&Geometry(obj,x,dx,dy)).ToArray();if(candidates.Length!=1)throw new InvalidOperationException("Source BD object lacks an unambiguous copied class/position/bounds match: "+Data.Text(obj,"class"));string uid=Data.Text(candidates[0],"uid");used.Add(uid);map.Add(Data.Text(obj,"uid"),uid);}}
        foreach(var d in s){bool root=Convert.ToInt32(d["depth"])==0;var actual=a.Single(x=>Data.Text(x,"uid")==diagrams[Data.Text(d,"uid")]);foreach(var wire in Rows(d,"wires")){var candidates=Rows(actual,"wires").Where(x=>!originalWires.Contains(Data.Text(x,"uid"))&&!usedWires.Contains(Data.Text(x,"uid"))&&WireMatches(wire,x,map,diagrams,terminals,terminalInverse,dx,dy,false)).ToArray();if(candidates.Length!=1)throw new InvalidOperationException("Source wire lacks an unambiguous copied endpoint topology match.");WireMatches(wire,candidates[0],map,diagrams,terminals,terminalInverse,dx,dy,true);usedWires.Add(Data.Text(candidates[0],"uid"));}}
        if(a.Sum(d=>Rows(d,"objects").Length)!=originalObjects.Count+used.Count||a.Sum(d=>Rows(d,"wires").Length)!=originalWires.Count+usedWires.Count)throw new InvalidOperationException("Merged native object/wire inventory contains missing or extra objects.");return map;
    }
    static bool WireMatches(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,string> objects,Dictionary<string,string> diagrams,Dictionary<string,string> terms,Dictionary<string,string> inverse,int dx,int dy,bool commit) {
        var expected=Rows(before,"terminals");var actual=Rows(after,"terminals");if(expected.Length!=actual.Length)return false;var local=new Dictionary<string,string>(terms,StringComparer.Ordinal);var localInverse=new Dictionary<string,string>(inverse,StringComparer.Ordinal);var owners=new Dictionary<string,string>(objects,StringComparer.Ordinal);var matched=new HashSet<string>(StringComparer.Ordinal);
        foreach(var endpoint in expected){var owner=Data.Map(endpoint["owner"]);string mapped;bool hasOwner=owners.TryGetValue(Data.Text(owner,"uid"),out mapped)||diagrams.TryGetValue(Data.Text(owner,"uid"),out mapped);string targetDiagram;if(!diagrams.TryGetValue(Data.Text(endpoint,"diagram_uid"),out targetDiagram))return false;var choices=actual.Where(x=>!matched.Contains(Data.Text(x,"uid"))&&Convert.ToBoolean(x["is_source"])==Convert.ToBoolean(endpoint["is_source"])&&Convert.ToString(x["name"])==Convert.ToString(endpoint["name"])&&Data.Json.Serialize(x["descriptor"])==Data.Json.Serialize(endpoint["descriptor"])&&Data.Text(x,"diagram_uid")==targetDiagram&&Data.Text(Data.Map(x["owner"]),"class")==Data.Text(owner,"class")&&(hasOwner?Data.Text(Data.Map(x["owner"]),"uid")==mapped:Geometry(owner,Data.Map(x["owner"]),dx,dy))).ToArray();if(choices.Length!=1)return false;string sourceUid=Data.Text(endpoint,"uid"),targetUid=Data.Text(choices[0],"uid"),old;if(objects.TryGetValue(sourceUid,out old)&&old!=targetUid||local.TryGetValue(sourceUid,out old)&&old!=targetUid||localInverse.TryGetValue(targetUid,out old)&&old!=sourceUid)return false;local[sourceUid]=targetUid;localInverse[targetUid]=sourceUid;matched.Add(targetUid);if(!hasOwner){string ownerUid=Data.Text(owner,"uid"),targetOwner=Data.Text(Data.Map(choices[0]["owner"]),"uid");if(owners.Any(p=>p.Key!=ownerUid&&p.Value==targetOwner))return false;owners[ownerUid]=targetOwner;}}
        if(commit){foreach(var pair in local)terms[pair.Key]=pair.Value;foreach(var pair in localInverse)inverse[pair.Key]=pair.Value;foreach(var pair in owners)if(!objects.ContainsKey(pair.Key))objects.Add(pair.Key,pair.Value);}return true;
    }
    static string[] Audit(Backend backend,object vi,Pins locks,Dictionary<string,GeneratedViDependency> generated,Dictionary<string,GeneratedTypeDependency> types,HashSet<string> seen,int depth) {
        if(depth>64)throw new InvalidDataException("Merge callee closure exceeds64 levels.");var direct=new List<string>();
        foreach(string value in backend.Callees(vi)) {
            string path=ViImport.CanonicalLocalPath(value);direct.Add(path);if(!seen.Add(path))continue;if(seen.Count>256)throw new InvalidDataException("Merge callee closure exceeds256 paths.");
            bool ni=NiDistribution.AcceptDependency(path,backend.NiRoot);
            if(ni)ViImport.AllowedCallee(path,backend.NiRoot);
            else {
                Recipe.CheckDependencyName(path,generated.Keys.Concat(types.Keys));
                if(Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase)||File.Exists(Path.Combine(Path.GetDirectoryName(path),"type-report.json"))) {GeneratedTypeDependency type;if(!types.TryGetValue(path,out type)){type=GeneratedTypeDependency.ValidateOne(path);locks.Add(type.Path);locks.Add(type.ReportPath);type.VerifyUnchanged();types.Add(path,type);}}
                else {GeneratedViDependency dependency;if(!generated.TryGetValue(path,out dependency)){dependency=GeneratedViDependency.ValidateOne(path);Pin(dependency,locks);generated.Add(path,dependency);}}
            }
            object child=backend.Open(path);Identity(backend,child,path);
            if(types.ContainsKey(path)){if(backend is NativeBackend){if(Convert.ToInt32(LabView.Invoke(child,"VIType",BindingFlags.GetProperty))!=types[path].VIType)throw new InvalidOperationException("Actual type VIType differs from immutable type report.");}if(backend.Callees(child).Length!=0)throw new ArgumentException("Merge type dependencies require an empty native callee list.");}
            else {if(!ni&&backend.State(child)!=1)throw new InvalidOperationException("Actual own VI dependency is not compiled idle.");Audit(backend,child,locks,generated,types,seen,depth+1);}
        }
        return direct.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static object RunRequest(string path,Func<Backend> createBackend) {
        Request r=ReadRequest(path);string reportPath=Path.Combine(r.Folder,"build-report.json"),failurePath=Path.Combine(r.Folder,"merge-failure.json");
        if(File.Exists(r.Target)||File.Exists(r.Scratch)||File.Exists(reportPath)||File.Exists(failurePath))throw new IOException("Merge never resumes or overwrites existing output.");
        using(var locks=new Pins()) {locks.Add(r.RequestPath);Pin(r.Source,locks);Pin(r.TargetSource,locks);PinCertificate(r,locks);Unchanged(r);
        var generated=new Dictionary<string,GeneratedViDependency>(StringComparer.OrdinalIgnoreCase);var types=new Dictionary<string,GeneratedTypeDependency>(StringComparer.OrdinalIgnoreCase);
        try {
            Storage.NoReparse(r.Target);Storage.NoReparse(r.Scratch);File.Copy(r.Source.Path,r.Scratch,false);File.Copy(r.TargetSource.Path,r.Target,false);
            if(Data.Hash(r.Scratch)!=r.Source.Sha256||Data.Hash(r.Target)!=r.TargetSource.Sha256)throw new IOException("Merge copies differ from snapshots.");
            using(Backend backend=createBackend()) {
                object source=backend.Open(r.Scratch),target=backend.Open(r.Target);Identity(backend,source,r.Scratch);Identity(backend,target,r.Target);
                if(backend.State(source)!=1||backend.State(target)!=1)throw new InvalidOperationException("Merge copies must be compiled idle.");
                var beforeClosure=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Audit(backend,source,locks,generated,types,beforeClosure,0);Audit(backend,target,locks,generated,types,beforeClosure,0);
                var sourceOriginalPanel=backend.Panel(r.Scratch);var targetPanel=backend.Panel(r.Target);object[] sourceOriginalDiagrams=backend.Diagrams(r.Scratch),targetDiagrams=backend.Diagrams(r.Target);object sourceConnector=backend.Connector(r.Scratch),targetConnector=backend.Connector(r.Target);
                var nameMapping=PlanSourceNames(sourceOriginalPanel,targetPanel,Path.GetFileNameWithoutExtension(r.Source.Path));foreach(var names in nameMapping)if(Convert.ToBoolean(names["renamed"]))names["source_terminal_uid"]=backend.RenameControl(r.Scratch,Data.Text(names,"source_uid"),Convert.ToString(names["new_label"]));
                var sourcePanel=backend.Panel(r.Scratch);ValidateSourceRenaming(sourceOriginalPanel,sourcePanel,nameMapping);object[] sourceDiagrams=backend.Diagrams(r.Scratch);ValidateRenameContent(sourceOriginalDiagrams,sourceDiagrams,nameMapping);if(Data.Json.Serialize(sourceConnector)!=Data.Json.Serialize(backend.Connector(r.Scratch)))throw new InvalidOperationException("Source prefix naming changed connector bindings.");ValidatePanelMerge(sourcePanel,targetPanel,null);
                int selected=backend.Merge(r.Scratch,r.Target,r.Dx,r.Dy);Identity(backend,source,r.Scratch);Identity(backend,target,r.Target);
                var afterPanel=backend.Panel(r.Target);ValidatePanelMerge(sourcePanel,targetPanel,afterPanel);object[] afterDiagrams=backend.Diagrams(r.Target);var objectMapping=ValidateContent(sourceDiagrams,targetDiagrams,afterDiagrams,r.Dx,r.Dy);
                if(Data.Json.Serialize(targetConnector)!=Data.Json.Serialize(backend.Connector(r.Target)))throw new InvalidOperationException("Target connector pattern or bound control identities changed.");
                if(Data.Json.Serialize(sourcePanel)!=Data.Json.Serialize(backend.Panel(r.Scratch))||Data.Json.Serialize(sourceDiagrams)!=Data.Json.Serialize(backend.Diagrams(r.Scratch))||Data.Json.Serialize(sourceConnector)!=Data.Json.Serialize(backend.Connector(r.Scratch)))throw new InvalidOperationException("Source copy actual controls/objects/topology changed during duplicate.");
                if(backend.State(target)!=1)throw new InvalidOperationException("Merged output is not compiled idle.");backend.Save(target,r.Target);Identity(backend,target,r.Target);
                if(backend.State(target)!=1)throw new InvalidOperationException("Saved merged output is not compiled idle.");
                var savedPanel=backend.Panel(r.Target);var savedDiagrams=backend.Diagrams(r.Target);ValidatePanelMerge(sourcePanel,targetPanel,savedPanel);ValidateContent(sourceDiagrams,targetDiagrams,savedDiagrams,r.Dx,r.Dy);
                if(Data.Json.Serialize(afterPanel)!=Data.Json.Serialize(savedPanel)||Data.Json.Serialize(afterDiagrams)!=Data.Json.Serialize(savedDiagrams)||Data.Json.Serialize(targetConnector)!=Data.Json.Serialize(backend.Connector(r.Target)))throw new InvalidOperationException("Saved native contents differ from verified merged snapshot.");
                var savedClosure=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string[] direct=Audit(backend,target,locks,generated,types,savedClosure,0);
                foreach(var d in generated.Values)d.VerifyUnchanged();foreach(var d in types.Values)d.VerifyUnchanged();Unchanged(r);
                if(Data.Hash(r.Scratch)!=r.Source.Sha256)throw new IOException("Source scratch bytes changed; source selection must remain unsaved.");
                object report=new {created=true,artifact_kind="merged_vi",vi_path=r.Target,vi_sha256=Data.Hash(r.Target),execution_state=1,labview_version=backend.Version,static_checks_passed=true,labview_called=true,generated_vi_executed=false,runtime_tested=false,execution_verified=false,complete_replacement=false,
                    source=r.Source.ToEvidence(),target_source=r.TargetSource.ToEvidence(),source_files_unchanged=true,source_reports_unchanged=true,source_originals_opened=false,source_originals_saved=false,source_scratch_path=r.Scratch,source_scratch_sha256=Data.Hash(r.Scratch),source_selection_saved=false,
                    content_snapshot_version=2,value_snapshot_version=3,source_certificate=r.SourceCertificate.Files,target_source_certificate=r.TargetCertificate.Files,actual_loaded_path=backend.LoadedPath(target),delta=new{x=r.Dx,y=r.Dy},selected_source_objects=selected,source_original_panel=sourceOriginalPanel,source_label_mapping=nameMapping,source_label_bounds_changes=sourceOriginalPanel.Where(o=>Data.Json.Serialize(o["bounds"])!=Data.Json.Serialize(sourcePanel.Single(a=>Data.Text(a,"uid")==Data.Text(o,"uid"))["bounds"])).Select(o=>new{source_uid=o["uid"],original_bounds=o["bounds"],renamed_bounds=sourcePanel.Single(a=>Data.Text(a,"uid")==Data.Text(o,"uid"))["bounds"],semantics="Total GObject bounds include the changed caption; original position anchor retained. Separate body rectangle not inferred."}).ToArray(),source_label_policy="Collisions/internal duplicates receive source prefix and stable UID suffix; blank names receive source UID name. Only unsaved source scratch is renamed; original target names/UID/value/default/geometry retained.",source_panel=sourcePanel,target_before_panel=targetPanel,target_after_panel=savedPanel,target_original_uids_preserved=true,target_original_positions_preserved=true,target_connector_preserved=true,source_controls_copied_one_to_one=true,source_copy_native_contents_preserved=true,source_object_uid_mapping=objectMapping,target_connector=targetConnector,
                    source_original_diagrams=sourceOriginalDiagrams,source_diagrams=sourceDiagrams,target_before_diagrams=targetDiagrams,target_after_diagrams=afterDiagrams,saved_direct_callees=direct,saved_dependency_closure=savedClosure.OrderBy(x=>x).ToArray(),checked_vi_dependencies=generated.Values.Select(d=>d.ToEvidence()).ToArray(),checked_type_dependencies=types.Values.Select(d=>d.ToEvidence()).ToArray(),helpers=backend.HelperEvidence,
                    limitations=new[]{"Actual native TD selects finite scalar/complex, bounded rank1/2 primitive Arrays and recursive known Cluster values; rank1 Cluster Arrays preserve full schema and ordered VARIANT records with a shared32768-node budget; opaque value types and unobserved Array element types remain rejected","FP class names alone do not reject supported native value schemas; Graph/Chart/Enum/Ring acceptance is recorded by their actual descriptor/value receipts; no style/class enumeration is inferred","Own immutable recipe/copy/import/merge provenance with bounded ancestry and actual contents; unknown non-control FP objects/multiple panes are rejected before duplicate; source connector assignment is not migrated","Only source scratch label collisions/duplicates/blanks are renamed; native root-label attributes must match the exact observed bounded Pascal suffix and all remaining TD bytes are preserved; total source caption bounds may change while its position anchor remains fixed","For/While/Case/Event diagrams, BD Decoration and wire endpoints are mapped with actual identities/geometry/type/direction; ambiguous physical mappings are rejected; LocalVariable binding has no independent native readback certificate","Actual saved static VI/CTL closure is checked; dynamic calls/DLL/runtime effects are not certified","Generic merged VIs are never run; native acceptance of new combinations is recorded separately"}};
                if(Encoding.UTF8.GetByteCount(Data.Json.Serialize(report))>1048576)throw new InvalidDataException("Merge evidence exceeds bounded generation-report size; partial output retained.");Storage.WriteNew(reportPath,report);return report;
            }
        }catch(Exception ex){if(!File.Exists(failurePath))Storage.WriteNew(failurePath,new{created=false,error=Data.Error(ex),error_chain=ex.ToString(),partial_path=r.Target,source_scratch_path=r.Scratch,complete_replacement=false});throw;}
        }
    }
}
}


