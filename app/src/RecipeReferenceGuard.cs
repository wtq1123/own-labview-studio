using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OwnLabView {
// Symbolic checks only, before any COM call. This deliberately models the migrated
// helper families, not the complete NI class tree. Array lengths, actual pin names,
// constant data representations and native validity still require runtime checks.
public static class RecipeReferenceGuard {
    sealed class Slot {
        public string Kind, Shape, Owner;
        public HashSet<string> Roots;
        public Slot(string kind,string shape) {Kind=kind;Shape=shape;}
    }
    sealed class Contract {
        public readonly Dictionary<string,string> Inputs=new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly Dictionary<string,Slot> Outputs=new Dictionary<string,Slot>(StringComparer.Ordinal);
        public string DeriveFrom;
        public Contract In(string field,string kind){Inputs.Add(field,kind);return this;}
        public Contract Out(string field,string kind,string shape="scalar"){Outputs.Add(field,new Slot(kind,shape));return this;}
        public Contract Derived(string field){DeriveFrom=field;return this;}
    }
    static readonly Regex Identifier=new Regex(@"\A[A-Za-z][A-Za-z0-9_]{0,39}\z");
    static readonly Regex Index=new Regex(@"\A(?:0|[1-9][0-9]*)\z");
    static Dictionary<string,object> Map(object o) {
        var map=o as Dictionary<string,object>;
        if(map==null)throw new ArgumentException("Expected recipe object");return map;
    }
    static IEnumerable<object> Items(object o) {
        var items=o as IEnumerable;
        if(items==null||o is string||o is IDictionary)throw new ArgumentException("Expected recipe array");
        return items.Cast<object>();
    }
    static string Text(Dictionary<string,object> o,string field) {
        object value;if(!o.TryGetValue(field,out value)||!(value is string))throw new ArgumentException("Expected text field "+field);
        return (string)value;
    }
    static Contract Schema(string helper) {
        var c=new Contract();
        switch(helper) {
        case "Native_AddControl_v2":return c.Out("object_id","Control").Out("terminal_id","Terminal");
        case "Own20261012_AddControlToOwner":return c.In("owner_id","Control").Out("object_id","Control");
        case "Own20261012_SetNamedBundlerElements":
        case "Own20261012_GetNamedBundlerElements":return c.In("bundler_id","Node").Out("element_names","Plain","array").Out("available_elements","Plain","array").Out("full_element_names","Plain","array").Derived("bundler_id");
        case "Own20261012_SetNamedUnbundlerElements":
        case "Own20261012_GetNamedUnbundlerElements":return c.In("unbundler_id","Node").Out("element_names","Plain","array").Out("available_elements","Plain","array").Out("full_element_names","Plain","array").Derived("unbundler_id");
        case "Native_AddNodeInDiagram":return c.In("owner_diagram_id","Diagram").Out("object_id","Node").Out("terminal_ids","Terminal","array");
        case "Own20261012_GuardedAddNodeInDiagram":return c.In("owner_diagram_id","Diagram").Out("object_id","Node").Out("terminal_ids","Terminal","array");
        case "Native_AddSubVIInDiagram":return c.In("owner_diagram_id","Diagram").Out("object_id","SubVI").Out("terminal_ids","Terminal","array");
        case "Managed_FilePlace":return c.In("owner_diagram_id","Diagram").Out("object_id","Node").Out("terminal_ids","Terminal","array").Out("owner_diagram_id","Diagram").Out("actual_class","Plain").Out("category","Plain").Out("source_path","Plain").Out("source_sha256","Plain").Out("terminals","Plain","array").Out("source_dependency_closure","Plain","array").Out("class_type_dependency","Plain").Out("source_files_unchanged","Plain").Out("source_saved","Plain").Out("source_executed","Plain").Out("helper_paths","Plain");
        case "Managed_CtlConstant":return c.In("owner_diagram_id","Diagram").Out("constant_id","Constant").Out("terminal_id","Terminal").Out("type_descriptor","Plain","array").Out("type_path","Plain").Out("type_sha256","Plain");
        case "Managed_GlobalNode":return c.In("owner_diagram_id","Diagram").Out("object_id","Node").Out("terminal_ids","Terminal","array").Out("terminal_id","Terminal").Out("control_name","Plain").Out("write","Plain").Out("direction_verified","Plain").Out("type_descriptor","Plain","array").Out("type_path","Plain").Out("type_sha256","Plain");
        case "Native_AddGrowableInDiagram":return c.In("owner_diagram_id","Diagram").Out("object_id","GrowableNode").Out("terminal_ids","Terminal","array");
        case "Native_AddInputChunk":return c.In("object_id","GrowableNode").Out("terminal_ids","Terminal","array").Derived("object_id");
        case "Native_AddFor":return c.In("owner_diagram_id","Diagram").Out("for_id","ForLoop");
        case "Native_AddWhile_v3":return c.In("owner_diagram_id","Diagram").Out("loop_id","WhileLoop");
        case "Native_AddCase":return c.In("owner_diagram_id","Diagram").Out("case_id","CaseStructure");
        case "Native_AddEvent":return c.In("owner_diagram_id","Diagram").Out("event_id","EventStructure");
        case "Native_GetForInfo":return c.In("for_id","ForLoop").Out("diagram_id","Diagram").Derived("for_id");
        case "Native_GetForTerminals":return c.In("for_id","ForLoop").Out("count_id","Terminal").Out("counter_id","Terminal").Derived("for_id");
        case "Native_GetWhileInfo_v3":return c.In("loop_id","WhileLoop").Out("diagram_id","Diagram").Out("stop_id","Terminal").Out("counter_id","Terminal").Derived("loop_id");
        case "Native_GetCaseInfo_v2":return c.In("case_id","CaseStructure").Out("frame_ids","Diagram","array").Out("terminal_ids","Terminal","array").Derived("case_id");
        case "Native_GetEventInfo_v2":return c.In("event_id","EventStructure").Out("frame_ids","Diagram","array").Out("terminal_ids","Terminal","array").Derived("event_id");
        case "Native_GetCaseFrameNames":return c.In("case_id","CaseStructure").Out("frame_names","Plain","array").Derived("case_id");
        case "Native_SetCaseFrameNames":return c.In("case_id","CaseStructure");
        case "Native_AddCaseFrame_v2":return c.In("case_id","CaseStructure").Out("frame_id","Diagram").Derived("case_id");
        case "Native_AddEventFrame_v2":return c.In("event_id","EventStructure").Out("frame_id","Diagram").Derived("event_id");
        case "Native_AddForShiftRegister":return c.In("for_id","ForLoop").Out("right_id","RightShiftRegister");
        case "Native_AddWhileShiftRegister":return c.In("loop_id","WhileLoop").Out("right_id","RightShiftRegister");
        case "Native_GetLeftShiftRefs":return c.In("right_id","RightShiftRegister").Out("left_ids","LeftShiftRegister","array").Derived("right_id");
        case "Native_GetTunnelTerminals_v2":return c.In("tunnel_id","Tunnel").Out("inside_ids","Terminal","array").Out("outside_id","Terminal").Derived("tunnel_id");
        case "Native_GetForTunnels":return c.In("for_id","ForLoop").Out("tunnel_ids","Tunnel","array").Derived("for_id");
        case "Native_SetLoopTunnelIndexMode":return c.In("tunnel_id","Tunnel");
        case "Native_GetNodeTerminals":return c.In("node_id","Node").Out("terminal_ids","Terminal","array").Derived("node_id");
        case "Own20261013_ScriptingAdd_V2":return c.In("owner_diagram_id","Diagram").In("source_x","Terminal").In("source_y","Terminal").Out("terminal_id","Terminal");
        case "Native_Wire":return c.In("source_terminal","Terminal").In("sink_terminal","Terminal");
        case "Native_MoveTerminal":return c.In("terminal_id","Terminal").In("owner_diagram_id","Diagram");
        case "Native_MoveNode":return c.In("object_id","Node").In("owner_diagram_id","Diagram");
        case "Native_DeleteGObject":return c.In("object_id","GObject");
        case "Native_CreateConstantFromTerminal_v2":
        case "Own20261012_CreateConstantFromTerminal":return c.In("terminal_id","Terminal").Out("constant_id","Constant").Out("created_terminal_id","Terminal");
        case "Native_SetDigitalNumericConstantValue":
        case "Native_SetStringConstantValue":return c.In("constant_id","Constant");
        case "Own20261012_SetDoubleConstantValue":return c.In("constant_id","Constant").Out("value","Plain").Out("type_descriptor","Plain","array");
        case "Own20261012_GetDigitalConstantValue":
        case "Native_GetStringConstantValue":return c.In("constant_id","Constant").Out("value","Plain");
        case "Managed_ControlDefault":return c.In("control_id","Control").Out("value","Plain").Out("class_name","Plain").Out("label","Plain");
        case "Managed_ArrayDefault":return c.In("array_id","Control").Out("value","Plain","array").Out("dimensions","Plain").Out("shape","Plain","array").Out("value_clr_type","Plain").Out("class_name","Plain").Out("label","Plain").Out("type_descriptor","Plain","array");
        case "Managed_ArrayMoveElement":return c.In("control_id","Control").In("owner_id","Control").Out("element_id","Control").Out("terminal_id","Terminal").Out("dimensions","Plain").Out("class_name","Plain").Out("element_identity","Plain").Out("is_indicator","Plain").Out("type_descriptor","Plain","array");
        case "Managed_BooleanText":
        case "Managed_BooleanTextInfo":return c.In("boolean_id","Control").Out("text","Plain").Out("multiple_strings","Plain").Out("mechanical_action","Plain").Out("state_strings","Plain","array").Out("label","Plain").Out("is_indicator","Plain").Out("value","Plain").Out("changed","Plain");
        case "Managed_NodeLabel":
        case "Managed_NodeLabelInfo":return c.In("node_id","LoopStructure").Out("label","Plain").Out("visible","Plain").Out("bounds","Plain","array").Out("class_name","Plain").Out("identity","Plain").Out("changed","Plain");
        case "Managed_ForCountInfo":return c.In("for_id","ForLoop").Out("count_id","Terminal").Out("inside_count_id","Terminal").Out("counter_id","Terminal").Out("diagram_id","Diagram").Out("tunnel_id","Tunnel").Out("type_descriptor","Plain","array").Derived("for_id");
        case "Managed_CaseSelectorInfo":return c.In("case_id","CaseStructure").Out("frame_ids","Diagram","array").Out("inside_ids","Terminal","array").Out("outside_id","Terminal").Out("selector_tunnel_id","Tunnel").Out("type_descriptor","Plain","array").Out("frame_identities","Plain","array").Derived("case_id");
        case "Managed_ObjectInfo":return c.In("object_id","GObject").Out("class_name","Plain").Out("bounds","Plain","array").Out("position","Plain","array").Out("identity","Plain");
        case "Managed_RootDecoration":return c.Out("object_id","Decoration").Out("class_name","Plain").Out("bounds","Plain","array").Out("position","Plain","array").Out("identity","Plain");
        case "Own20261012_SetArrayDimensions":return c.In("array_id","Control").Out("dimensions","Plain");
        case "Own20261012_GetArrayInfo":return c.In("array_id","Control").Out("dimensions","Plain").Out("element_id","Control").Out("terminal_id","Terminal").Derived("array_id");
        case "Own20261012_SetForStopIfTrue":
        case "Own20261012_GetForStopIfTrue":return c.In("for_id","ForLoop").Out("stop_if_true","Plain");
        case "Own20261012_SetWhileStopIfTrue":
        case "Own20261012_GetWhileStopIfTrue":return c.In("while_id","WhileLoop").Out("stop_if_true","Plain");
        case "Own20261012_SetLoopTunnelConditional":
        case "Own20261012_GetLoopTunnelConditional":return c.In("tunnel_id","Tunnel").Out("is_conditional","Plain");
        case "Own20261012_GetLoopTunnelConditionTerminal":return c.In("tunnel_id","Tunnel").Out("condition_terminal_id","Terminal").Derived("tunnel_id");
        case "Native_CreateControlFromTerminal_v2":
        case "Native_CreateIndicatorFromTerminal":
        case "Own20261012_CreateControlFromTerminal":
        case "Own20261012_CreateIndicatorFromTerminal":return c.In("terminal_id","Terminal").Out("control_id","Control").Out("created_terminal_id","Terminal");
        case "Native_SetControlLabel":
        case "Native_SetControlPosition":return c.In("control_id","Control");
        // A generic Control may be numeric; actual numeric representation remains
        // a native property check. The guard does not invent a style/class mapping.
        case "Native_SetNumericRepresentation":return c.In("numeric_id","Control");
        case "Own20261012_GetNumericRepresentation":return c.In("numeric_id","Control").Out("representation","Plain").Out("representation_name","Plain").Out("terminal_id","Terminal").Out("type_descriptor","Plain","array").Derived("numeric_id");
        case "Native_AssignConnectorTerminal":return c.In("control_id","Control");
        case "Managed_ConnectSourceToStructure":return c.In("structure_id","Structure").In("source_terminal","Terminal").Out("tunnel_id","Tunnel").Out("inside_ids","Terminal","array").Out("outside_id","Terminal").Out("structure_class","Plain").Out("tunnel_identity","Plain");
        case "Managed_ConnectorAuto":
        case "Managed_ConnectorInfo":return c.Out("pattern","Plain").Out("terminal_count","Plain").Out("terminal_bounds","Plain","array").Out("assignments","Plain","array");
        case "Native_GetConnectorControls":return c.Out("control_ids","Control","array").Derived("root");
        case "Native_CreateLocalVariable_v5":return c.In("control_id","Control").Out("local_id","Node").Out("terminal_ids","Terminal","array");
        case "Native_GetDiagramObjects":return c.In("diagram_id","Diagram").Out("object_ids","GObject","array").Derived("diagram_id");
        case "Native_GetDiagramWires":return c.In("diagram_id","Diagram").Out("wire_ids","Wire","array").Derived("diagram_id");
        case "Native_GetWireBroken":return c.In("wire_id","Wire").Out("is_broken","Plain").Derived("wire_id");
        case "Native_GetObjectInfo":return c.In("object_id","GObject").Out("class_name","Plain").Out("bounds","Plain","array");
        case "Own20261012_SetControlIndicator":return c.In("control_id","Control");
        case "Own20261012_GetControlIndicator":return c.In("control_id","Control").Out("is_indicator","Plain");
        case "Native_GetControlTerminal":return c.In("control_id","Control").Out("terminal_id","Terminal").Derived("control_id");
        case "Managed_FindControl":return c.Out("control_id","Control").Out("terminal_id","Terminal").Derived("root");
        case "Own20261012_GetPropertyInfo":return c.In("property_id","Node").Out("class_name","Plain").Out("supported_properties","Plain","array").Out("property_count","Plain").Out("property_item_ids","PropertyItem","array").Out("properties","Plain","array").Derived("property_id");
        case "Own20261012_SetPropertyClass":return c.In("property_id","Node");
        case "Own20261012_SelectProperties":return c.In("property_id","Node").Out("property_item_ids","PropertyItem","array").Derived("property_id");
        case "Own20261012_GetPropertyItemInfo":return c.In("property_item_id","PropertyItem").Out("is_write","Plain").Out("property","Plain","array").Derived("property_item_id");
        case "Own20261012_SetPropertyItemWrite":
        case "Own20261012_SetPropertyItem":return c.In("property_item_id","PropertyItem");
        case "Own20261012_GetInvokeInfo":return c.In("invoke_id","Node").Out("class_name","Plain").Out("method","Plain","array").Out("supported_methods","Plain","array").Derived("invoke_id");
        case "Own20261012_SetInvokeClass":
        case "Own20261012_SelectMethod":return c.In("invoke_id","Node");
        case "Own20261012_GetInvokeTerminals":return c.In("invoke_id","Node").Out("input_terminal_ids","Terminal","array").Out("output_terminal_ids","Terminal","array").Derived("invoke_id");
        case "Own20261012_CreateControlReference":return c.In("control_id","Control").Out("reference_constant_id","Node");
        case "Own20261012_SetFormulaExpression":return c.In("formula_id","Node");
        case "Own20261012_GetFormulaInfo":return c.In("formula_id","Node").Out("expression","Plain").Out("parameter_ids","FormulaParameter","array").Derived("formula_id");
        case "Own20261012_FormulaAddInput":
        case "Own20261012_FormulaAddOutput":return c.In("formula_id","Node").Out("parameter_id","FormulaParameter").Derived("formula_id");
        case "Own20261012_GetFormulaParameterInfo":return c.In("parameter_id","FormulaParameter").Out("name","Plain").Out("is_input","Plain").Out("terminal_id","Terminal").Derived("parameter_id");
        case "Own20261012_SetFormulaParameterName":
        case "Own20261012_DeleteFormulaParameter":return c.In("parameter_id","FormulaParameter");
        case "Own20261012_SetForConditional":return c.In("for_id","ForLoop");
        case "Own20261012_GetForStop":return c.In("for_id","ForLoop").Out("stop_id","Terminal").Derived("for_id");
        case "Own20261012_CleanDiagram":return c;
        default:throw new ArgumentException("No reference contract for helper "+helper);
        }
    }
    static bool Compatible(string actual,string expected) {
        if(actual=="Plain")return false;
        if(actual==expected)return true;
        if(expected=="LoopStructure")return actual=="ForLoop"||actual=="WhileLoop";
        if(expected=="GObject")return true;
        if(expected=="Structure")return actual=="ForLoop"||actual=="WhileLoop"||actual=="CaseStructure"||actual=="EventStructure";
        if(expected=="Node")return actual=="SubVI"||actual=="GrowableNode"||actual=="ForLoop"||actual=="WhileLoop"||actual=="CaseStructure"||actual=="EventStructure"||actual=="Constant";
        if(expected=="Tunnel")return actual=="RightShiftRegister"||actual=="LeftShiftRegister";
        return false;
    }
    static Slot Resolve(string reference,Dictionary<string,Dictionary<string,Slot>> known,HashSet<string> dead) {
        if(reference==null||reference.Length>512||!reference.StartsWith("$",StringComparison.Ordinal))throw new ArgumentException("Reference must be symbolic");
        string[] p=reference.Substring(1).Split('.');
        if(p.Length<2||p.Length>3||p.Any(String.IsNullOrEmpty)||!Identifier.IsMatch(p[0]))throw new ArgumentException("Invalid reference syntax: "+reference);
        Dictionary<string,Slot> outputs;Slot slot;
        if(!known.TryGetValue(p[0],out outputs)||!outputs.TryGetValue(p[1],out slot))throw new ArgumentException("Unknown/forward output reference: "+reference);
        if(slot.Kind=="Plain")throw new ArgumentException("Output is data, not a native reference: "+reference);
        if(slot.Shape=="scalar"&&p.Length!=2)throw new ArgumentException("Scalar reference cannot be indexed: "+reference);
        if(slot.Shape=="array") {
            int i;
            if(p.Length!=3||!Index.IsMatch(p[2])||!Int32.TryParse(p[2],NumberStyles.None,CultureInfo.InvariantCulture,out i))throw new ArgumentException("Reference array requires one nonnegative canonical index: "+reference);
        }
        if(slot.Shape=="pins") {
            if(p.Length!=3||String.IsNullOrWhiteSpace(p[2])||p[2].Any(Char.IsControl))throw new ArgumentException("Pin reference requires a name or index: "+reference);
            // Prevent misspelled/negative indexes from silently becoming pin names.
            if(Regex.IsMatch(p[2],@"\A[+-]?[0-9]+\z")) {
                int i;if(!Index.IsMatch(p[2])||!Int32.TryParse(p[2],NumberStyles.None,CultureInfo.InvariantCulture,out i))throw new ArgumentException("Invalid pin index: "+reference);
            }
        }
        if(slot.Roots.Any(dead.Contains))throw new ArgumentException("Reference was invalidated by deletion or structure mutation: "+reference);
        return slot;
    }
    sealed class Analysis {
        public Dictionary<string,Dictionary<string,Slot>> Known;
        public HashSet<string> Dead;
        public List<string> Reasons;
    }
    public static void Validate(Dictionary<string,object> doc,Dictionary<string,object> catalog) { Analyze(doc,catalog); }
    // Arrays are returned unindexed so the worker can expand the actual NI array.
    // The root is omitted: the worker owns its separate root-diagram audit.
    public static string[] LiveDiagramReferences(Dictionary<string,object> doc,Dictionary<string,object> catalog) {
        return Diagrams(Analyze(doc,catalog),true);
    }
    public static Dictionary<string,object> DiagramAuditScope(Dictionary<string,object> doc,Dictionary<string,object> catalog) {
        var a=Analyze(doc,catalog);var omitted=Diagrams(a,false);
        return new Dictionary<string,object>{{"audit_complete",omitted.Length==0&&a.Reasons.Count==0},
            {"omitted_diagram_references",omitted},{"reasons",a.Reasons.ToArray()},
            {"scope","Known symbolic diagram lifetimes only; root is audited separately and unobserved structure frames are not discovered."}};
    }
    static string[] Diagrams(Analysis a,bool live) {
        var result=new List<string>();
        foreach(var producer in a.Known)if(producer.Key!="root")foreach(var field in producer.Value)
            if(field.Value.Kind=="Diagram"&&!field.Value.Roots.Any(a.Dead.Contains)==live)result.Add("$"+producer.Key+"."+field.Key);
        return result.ToArray();
    }
    static Analysis Analyze(Dictionary<string,object> doc,Dictionary<string,object> catalog) {
        if(doc==null||catalog==null)throw new ArgumentException("Recipe document and catalog are required");
        var known=new Dictionary<string,Dictionary<string,Slot>>(StringComparer.Ordinal);
        var root=new Slot("Diagram","scalar"){Owner="root",Roots=new HashSet<string>(StringComparer.Ordinal){"root"}};
        known["root"]=new Dictionary<string,Slot>(StringComparer.Ordinal){{"diagram_id",root}};
        var dead=new HashSet<string>(StringComparer.Ordinal);
        var caseOwners=new HashSet<string>(StringComparer.Ordinal);
        var configurableOwners=new HashSet<string>(StringComparer.Ordinal);
        var namedOwners=new HashSet<string>(StringComparer.Ordinal);
        var containerParents=new Dictionary<string,string>(StringComparer.Ordinal);
        var boundReferenceOwners=new Dictionary<string,string>(StringComparer.Ordinal);
        var connectedSources=new HashSet<string>(StringComparer.Ordinal);
        var containerOwners=new HashSet<string>(StringComparer.Ordinal);
        var arrayOwners=new HashSet<string>(StringComparer.Ordinal);var arrayElementOwners=new HashSet<string>(StringComparer.Ordinal);
        var freshTopControls=new HashSet<string>(StringComparer.Ordinal);var connectedControlOwners=new HashSet<string>(StringComparer.Ordinal);
        var childLabels=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        var reasons=new List<string>();
        foreach(object value in Items(doc["steps"])) {
            var step=Map(value);string id=Text(step,"id"),op=Text(step,"op");
            if(!Identifier.IsMatch(id)||known.ContainsKey(id))throw new ArgumentException("Invalid/duplicate step id: "+id);
            object declared;if(!catalog.TryGetValue(op,out declared))throw new ArgumentException("Unknown operation: "+op);
            var spec=Map(declared);var signature=Map(spec["inputs"]);var args=Map(step["args"]);
            string helper=Text(spec,"helper");Contract c=Schema(helper);
            var refFields=new HashSet<string>(signature.Where(x=>Convert.ToString(x.Value)=="ref").Select(x=>x.Key),StringComparer.Ordinal);
            if(!refFields.SetEquals(c.Inputs.Keys))throw new ArgumentException("Reference contract differs from catalog: "+op);
            if(!new HashSet<string>(Items(spec["outputs"]).Select(Convert.ToString),StringComparer.Ordinal).SetEquals(c.Outputs.Keys))throw new ArgumentException("Output contract differs from catalog: "+op);
            var inputs=new Dictionary<string,Slot>(StringComparer.Ordinal);
            var roots=new HashSet<string>(StringComparer.Ordinal){"root"};
            foreach(var field in c.Inputs) {
                Slot r=Resolve(Text(args,field.Key),known,dead);
                if(!Compatible(r.Kind,field.Value))throw new ArgumentException("Reference kind mismatch for "+id+"."+field.Key+": expected "+field.Value+", got "+r.Kind);
                roots.UnionWith(r.Roots);inputs[field.Key]=r;
            }
            if(dead.Contains("root"))throw new ArgumentException("Recipe scope invalidated by deletion");
            if(new[]{"Own20261013_ScriptingAdd_V2","Native_Wire","Native_AssignConnectorTerminal","Native_CreateLocalVariable_v5","Own20261012_CreateControlReference","Managed_ConnectSourceToStructure","Native_MoveTerminal","Native_CreateConstantFromTerminal_v2","Own20261012_CreateConstantFromTerminal","Native_CreateControlFromTerminal_v2","Native_CreateIndicatorFromTerminal","Own20261012_CreateControlFromTerminal","Own20261012_CreateIndicatorFromTerminal"}.Contains(helper))
                foreach(string control in freshTopControls)if(inputs.Values.Any(s=>s.Owner==control||s.Roots.Contains(control)))connectedControlOwners.Add(control);
            if(helper=="Managed_ConnectorAuto")connectedControlOwners.UnionWith(freshTopControls);
            if(helper=="Managed_ArrayMoveElement") {
                string source=inputs["control_id"].Owner,parent=inputs["owner_id"].Owner;
                if(source==parent||!freshTopControls.Contains(source)||containerParents.ContainsKey(source)||connectedControlOwners.Contains(source)||!arrayOwners.Contains(parent)||!arrayElementOwners.Add(parent))throw new ArgumentException("Move a newly created free top-level control into one empty new Array before wiring, reference binding or connector assignment.");
                containerParents[source]=parent;containerOwners.Add(parent);
                foreach(var producer in known)foreach(var slot in producer.Value.Values)if(slot.Owner==source||slot.Roots.Contains(source)) {slot.Roots.UnionWith(inputs["owner_id"].Roots);if(slot.Kind=="Terminal")dead.Add("terminal-generation:"+producer.Key);}
                InvalidateContainerBindings(known,dead,parent,containerParents,namedOwners);InvalidateBoundControlBindings(known,dead,parent,containerParents,boundReferenceOwners,configurableOwners);
            }
            if(helper=="Managed_ConnectSourceToStructure") {
                string pair=Text(args,"structure_id")+"\n"+Text(args,"source_terminal");if(!connectedSources.Add(pair))throw new ArgumentException("Repeated source/structure request is not permitted.");
                string changedOwner=inputs["structure_id"].Owner;
                foreach(var producer in known)foreach(var slot in producer.Value.Values)
                    if(slot.Roots.Contains(changedOwner)&&slot.Kind=="Terminal") {dead.Add("terminal-generation:"+producer.Key);dead.Add("tunnel-terminal-generation:"+producer.Key);}
            }
            if(inputs.ContainsKey("bundler_id")||inputs.ContainsKey("unbundler_id")) {
                string namedOwner=inputs.Values.First().Owner;namedOwners.Add(namedOwner);configurableOwners.Add(namedOwner);
                if(helper=="Own20261012_SetNamedBundlerElements"||helper=="Own20261012_SetNamedUnbundlerElements")
                    foreach(string affected in namedOwners)InvalidateNodeBindings(known,dead,affected);
            }
            if(helper=="Native_AddInputChunk") {
                string grownOwner=inputs["object_id"].Owner;InvalidateNodeBindings(known,dead,grownOwner);
                if(namedOwners.Contains(grownOwner))foreach(string affected in namedOwners)InvalidateNodeBindings(known,dead,affected);
            }
            if(inputs.ContainsKey("terminal_id")&&namedOwners.Contains(inputs["terminal_id"].Owner)&&
               (helper=="Own20261012_CreateControlFromTerminal"||helper=="Own20261012_CreateIndicatorFromTerminal"||helper=="Own20261012_CreateConstantFromTerminal"||
                helper=="Native_CreateControlFromTerminal_v2"||helper=="Native_CreateIndicatorFromTerminal"||helper=="Native_CreateConstantFromTerminal_v2")) {
                foreach(string affected in namedOwners)InvalidateNodeBindings(known,dead,affected);
                // New objects are produced after the implicit connection. They
                // retain owner/deletion dependencies but not its old terminal epoch.
                roots.RemoveWhere(token=>token.StartsWith("terminal-generation:",StringComparison.Ordinal)&&dead.Contains(token));
            }
            if(helper=="Own20261012_AddControlToOwner") {
                string parent=inputs["owner_id"].Owner;containerOwners.Add(parent);containerParents[id]=parent;
                if(op=="array_element"&&(!arrayOwners.Contains(parent)||!arrayElementOwners.Add(parent)))throw new ArgumentException("Create exactly one element in an Array created by this recipe.");
                object childLabel;
                if(args.TryGetValue("label",out childLabel)) {
                    HashSet<string> labels;if(!childLabels.TryGetValue(parent,out labels))childLabels[parent]=labels=new HashSet<string>(StringComparer.Ordinal);
                    if(!labels.Add(Convert.ToString(childLabel)))throw new ArgumentException("Duplicate Cluster child label in this recipe: "+childLabel);
                }
                InvalidateContainerBindings(known,dead,parent,containerParents,namedOwners);
                InvalidateBoundControlBindings(known,dead,parent,containerParents,boundReferenceOwners,configurableOwners);
            }
            string changedControl=null;
            if(helper=="Native_SetControlLabel"||helper=="Own20261012_SetControlIndicator")changedControl=inputs["control_id"].Owner;
            if(helper=="Native_SetNumericRepresentation")changedControl=inputs["numeric_id"].Owner;
            if(helper=="Own20261012_SetArrayDimensions")changedControl=inputs["array_id"].Owner;
            if(helper=="Native_DeleteGObject"&&inputs["object_id"].Kind=="Control")changedControl=inputs["object_id"].Owner;
            if(changedControl!=null&&(changedControl=="root"||containerParents.ContainsKey(changedControl)||containerOwners.Contains(changedControl)))
                InvalidateContainerBindings(known,dead,changedControl,containerParents,namedOwners);
            if(changedControl!=null)InvalidateBoundControlBindings(known,dead,changedControl,containerParents,boundReferenceOwners,configurableOwners);
            if(inputs.ContainsKey("formula_id")||inputs.ContainsKey("parameter_id")) {
                string formulaOwner=inputs.Values.First().Owner;
                configurableOwners.Add(formulaOwner);
                if(helper=="Own20261012_SetFormulaExpression"||helper=="Own20261012_FormulaAddInput"||helper=="Own20261012_FormulaAddOutput"||
                   helper=="Own20261012_SetFormulaParameterName"||helper=="Own20261012_DeleteFormulaParameter")
                    InvalidateNodeBindings(known,dead,formulaOwner);
            }
            if(helper.StartsWith("Own20261012_",StringComparison.Ordinal) &&
               (inputs.ContainsKey("property_id")||inputs.ContainsKey("invoke_id")||inputs.ContainsKey("property_item_id"))) {
                string configuredOwner=inputs.Values.First().Owner;
                configurableOwners.Add(configuredOwner);
                if(helper=="Own20261012_SetPropertyClass"||helper=="Own20261012_SelectProperties"||
                   helper=="Own20261012_SetPropertyItem"||helper=="Own20261012_SetPropertyItemWrite"||
                   helper=="Own20261012_SetInvokeClass"||helper=="Own20261012_SelectMethod")
                    InvalidateNodeBindings(known,dead,configuredOwner);
            }
            if(helper=="Own20261012_SetControlIndicator"||helper=="Native_SetNumericRepresentation") {
                string changedOwner=inputs[helper=="Native_SetNumericRepresentation"?"numeric_id":"control_id"].Owner;
                foreach(var producer in known)foreach(var slot in producer.Value.Values)
                    if(slot.Kind=="Terminal"&&(changedOwner=="root"||slot.Owner==changedOwner||slot.Owner=="root"))
                        dead.Add("terminal-generation:"+producer.Key);
            }
            if(helper=="Own20261012_SetForConditional") {
                string changedOwner=inputs["for_id"].Owner;
                foreach(var producer in known)if(producer.Value.ContainsKey("stop_id")&&producer.Value["stop_id"].Owner==changedOwner)dead.Add(producer.Key);
            }
            if(helper=="Own20261012_SetLoopTunnelConditional"||helper=="Native_SetLoopTunnelIndexMode") {
                string changedOwner=inputs["tunnel_id"].Owner;
                foreach(var producer in known)foreach(var slot in producer.Value.Values)
                    if(slot.Owner==changedOwner||slot.Owner=="root")dead.Add("tunnel-terminal-generation:"+producer.Key);
            }
            if(helper=="Native_Wire"||helper=="Own20261013_ScriptingAdd_V2") {
                // A reference wire can specialize a generic VI Server node to a
                // strict control class. Conservatively refresh after every wire
                // touching a configured node, including numeric pin aliases.
                foreach(string configuredOwner in inputs.Values.Select(s=>s.Owner).Where(configurableOwners.Contains).Distinct())
                    InvalidateNodeBindings(known,dead,configuredOwner);
                // A named node can propagate a changed Cluster type to another
                // named node. Refresh the entire known named-node set rather than
                // pretending individual raw wire aliases identify that propagation.
                if(inputs.Values.Any(s=>namedOwners.Contains(s.Owner)))
                    foreach(string affected in namedOwners)InvalidateNodeBindings(known,dead,affected);
                // Connecting a selector can change Case frame types and rebuild
                // diagrams. We cannot identify the exact pin statically, so any
                // directly Case-owned terminal invalidates previous frame/term
                // generations. Reacquiring case_info produces fresh aliases.
                foreach(string caseOwner in inputs.Values.Where(s=>s.Kind=="Terminal"&&caseOwners.Contains(s.Owner)).Select(s=>s.Owner).Distinct()) {
                    foreach(var producer in known)if(producer.Key!=caseOwner&&producer.Value.Values.Any(s=>s.Owner==caseOwner))dead.Add(producer.Key);
                    reasons.Add("Step "+id+" wires a CaseStructure terminal; earlier derived references for "+caseOwner+" may have been rebuilt.");
                }
            }
            if(helper=="Native_AddCaseFrame_v2"||helper=="Native_SetCaseFrameNames") {
                string caseOwner=inputs["case_id"].Owner;
                foreach(var producer in known)if(producer.Key!=caseOwner&&producer.Value.Values.Any(s=>s.Owner==caseOwner))dead.Add(producer.Key);
                reasons.Add("Step "+id+" mutates Case frames/names; earlier derived references for "+caseOwner+" require reacquisition.");
            }
            if(helper=="Native_MoveNode"||helper=="Native_MoveTerminal") {
                Slot moved=inputs[helper=="Native_MoveNode"?"object_id":"terminal_id"];
                var newRoots=inputs["owner_diagram_id"].Roots;
                // Keep old dependencies too: a move can create/rewrite tunnels.
                // This is conservative and does not pretend to resolve NI aliases.
                foreach(var producer in known.Values)foreach(var output in producer.Values)
                    if(output.Roots.Contains(moved.Owner))output.Roots.UnionWith(newRoots);
            }
            if(helper=="Native_DeleteGObject") {
                Slot deleted=inputs["object_id"];
                if(deleted==root)throw new ArgumentException("Cannot delete the root diagram");
                // Derived aliases share their originating owner's token. Deleting
                // an inspected unknown object conservatively invalidates its whole
                // inspected scope, since static analysis cannot identify aliases.
                if(deleted.Owner=="root") {
                    // An inspected reference can alias any known object. Keep
                    // the actual root diagram available for fresh inspection.
                    foreach(string producer in known.Keys.Where(k=>k!="root"))dead.Add(producer);
                } else dead.Add(deleted.Owner);
                foreach(var producer in known)if(producer.Key!="root"&&producer.Value.Values.Any(s=>s.Owner=="root"&&s.Kind!="Plain"))dead.Add(producer.Key);
                if(deleted.Kind=="GObject"||deleted.Kind=="Wire"||deleted.Kind=="Diagram")
                    reasons.Add("Step "+id+" deletes an inspected object or diagram; aliases in owner scope "+deleted.Owner+" are conservatively excluded.");
            }
            string owner=id;
            if(c.DeriveFrom!=null)owner=c.DeriveFrom=="root"?"root":inputs[c.DeriveFrom].Owner;
            roots.Add(owner);
            // Derived handles have a generation token as well as their owner.
            // Descendants inherit it; newly reacquired aliases do not.
            roots.Add(id);
            var outputs=new Dictionary<string,Slot>(StringComparer.Ordinal);
            foreach(var field in c.Outputs) {
                var output=new Slot(field.Value.Kind,field.Value.Shape){Owner=owner,Roots=new HashSet<string>(roots,StringComparer.Ordinal)};
                if(output.Kind=="Terminal")output.Roots.Add("terminal-generation:"+id);
                if(output.Kind=="PropertyItem")output.Roots.Add("property-generation:"+id);
                if(output.Kind=="FormulaParameter")output.Roots.Add("formula-generation:"+id);
                if((helper=="Own20261012_GetArrayInfo"||helper=="Managed_ArrayMoveElement")&&output.Kind!="Plain")output.Roots.Add("array-generation:"+id);
                if((helper=="Own20261012_GetLoopTunnelConditionTerminal"||helper=="Native_GetTunnelTerminals_v2")&&output.Kind=="Terminal")output.Roots.Add("tunnel-terminal-generation:"+id);
                outputs[field.Key]=output;
            }
            if(outputs.ContainsKey("terminal_ids"))outputs["pins"]=new Slot("Terminal","pins"){Owner=owner,Roots=new HashSet<string>(outputs["terminal_ids"].Roots,StringComparer.Ordinal)};
            // Nested controls have no independent BD Terminal. Metadata remains
            // available, but a downstream alias must not invent one.
            if(helper=="Own20261012_GetNumericRepresentation"&&containerParents.ContainsKey(inputs["numeric_id"].Owner))outputs.Remove("terminal_id");
            if(helper=="Managed_ArrayMoveElement") {outputs["element_id"].Owner=inputs["control_id"].Owner;outputs["terminal_id"].Owner=inputs["owner_id"].Owner;}
            known[id]=outputs;
            if(helper=="Native_AddControl_v2")freshTopControls.Add(id);
            if(helper=="Own20261012_CreateControlReference")boundReferenceOwners[id]=inputs["control_id"].Owner;
            if(outputs.Values.Any(s=>s.Kind=="CaseStructure"))caseOwners.Add(owner);
            object style;
            if(args.TryGetValue("style",out style)&&outputs.ContainsKey("object_id")) {
                if(Convert.ToString(style)=="Cluster"||Convert.ToString(style)=="Array")containerOwners.Add(owner);
                if(helper=="Native_AddControl_v2"&&Convert.ToString(style)=="Array")arrayOwners.Add(owner);
            }
            if((helper=="Native_AddNodeInDiagram"||helper=="Own20261012_GuardedAddNodeInDiagram"||helper=="Native_AddGrowableInDiagram")&&args.TryGetValue("style",out style)&&
               (Convert.ToString(style)=="Bundle By Name"||Convert.ToString(style)=="Unbundle By Name")) {
                outputs["object_id"].Kind="GrowableNode";namedOwners.Add(owner);configurableOwners.Add(owner);
            }
            if((helper=="Native_AddNodeInDiagram"||helper=="Own20261012_GuardedAddNodeInDiagram"||helper=="Native_AddGrowableInDiagram")&&args.TryGetValue("style",out style)&&
               (Convert.ToString(style)=="Property Node"||Convert.ToString(style)=="Invoke Node"||Convert.ToString(style)=="Formula Node"))configurableOwners.Add(owner);
        }
        return new Analysis{Known=known,Dead=dead,Reasons=reasons};
    }

    static void InvalidateBoundControlBindings(Dictionary<string,Dictionary<string,Slot>> known,HashSet<string> dead,string owner,Dictionary<string,string> parents,Dictionary<string,string> references,HashSet<string> configuredNodes) {
        var affected=new HashSet<string>(StringComparer.Ordinal);string next=owner;
        while(affected.Add(next)&&parents.TryGetValue(next,out next)) {}
        bool any=false;
        foreach(var reference in references)if(owner=="root"||reference.Value=="root"||affected.Contains(reference.Value)) {InvalidateNodeBindings(known,dead,reference.Key);any=true;}
        // A strict reference wire can propagate a new scalar/array/Cluster type
        // through an already configured Property/Invoke node. Keep primary
        // objects but require fresh terminal/item metadata after that change.
        if(any)foreach(string node in configuredNodes)InvalidateNodeBindings(known,dead,node);
    }
    static void InvalidateContainerBindings(Dictionary<string,Dictionary<string,Slot>> known,HashSet<string> dead,string owner,Dictionary<string,string> parents,HashSet<string> namedOwners) {
        var affected=new HashSet<string>(StringComparer.Ordinal);string next=owner;
        while(affected.Add(next)&&parents.TryGetValue(next,out next)) {}
        foreach(var producer in known)foreach(var slot in producer.Value.Values)
            if(owner=="root"||slot.Owner=="root"||affected.Contains(slot.Owner)||affected.Any(slot.Roots.Contains)) {
                if(slot.Kind=="Terminal")dead.Add("terminal-generation:"+producer.Key);
                dead.Add("array-generation:"+producer.Key);
            }
        foreach(string named in namedOwners)InvalidateNodeBindings(known,dead,named);
    }
    static void InvalidateNodeBindings(Dictionary<string,Dictionary<string,Slot>> known,HashSet<string> dead,string owner) {
        foreach(var producer in known)foreach(var slot in producer.Value.Values)
            if(owner=="root"||slot.Owner==owner||slot.Owner=="root") {
                if(slot.Kind=="Terminal")dead.Add("terminal-generation:"+producer.Key);
                if(slot.Kind=="PropertyItem")dead.Add("property-generation:"+producer.Key);
                if(slot.Kind=="FormulaParameter")dead.Add("formula-generation:"+producer.Key);
            }
    }

    static Dictionary<string,object> Step(string id,string op,params string[] refs) {
        var a=new Dictionary<string,object>();for(int i=0;i<refs.Length;i+=2)a.Add(refs[i],refs[i+1]);
        return new Dictionary<string,object>{{"id",id},{"op",op},{"args",a}};
    }
    public static object SelfTest(Dictionary<string,object> catalog) {
        var results=new List<object>();int failures=0;
        Action<string,bool,object[]> check=(name,accept,steps)=> {
            string error=null;try{Validate(new Dictionary<string,object>{{"steps",steps}},catalog);}catch(ArgumentException ex){error=ex.Message;}
            bool passed=(error==null)==accept;if(!passed)failures++;results.Add(new{name,passed,error});
        };
        Func<Dictionary<string,object>> node=()=>Step("n","node","owner_diagram_id","$root.diagram_id");
        check("MIT add accepts two live terminal sources",true,new object[]{Step("a","control"),Step("b","control"),Step("sum","scripting_add","owner_diagram_id","$root.diagram_id","source_x","$a.terminal_id","source_y","$b.terminal_id")});
        check("MIT add rejects diagram as source",false,new object[]{Step("b","control"),Step("sum","scripting_add","owner_diagram_id","$root.diagram_id","source_x","$root.diagram_id","source_y","$b.terminal_id")});
        check("MIT add rejects deleted input",false,new object[]{Step("a","control"),Step("b","control"),Step("remove","delete","object_id","$a.object_id"),Step("sum","scripting_add","owner_diagram_id","$root.diagram_id","source_x","$a.terminal_id","source_y","$b.terminal_id")});
        check("named and indexed terminal aliases",true,new object[]{node(),Step("w","wire","source_terminal","$n.pins.x*y","sink_terminal","$n.terminal_ids.0")});
        check("node as diagram rejected",false,new object[]{node(),Step("bad","node","owner_diagram_id","$n.object_id")});
        check("diagram as control rejected",false,new object[]{Step("bad","label","control_id","$root.diagram_id")});
        check("unknown output rejected",false,new object[]{node(),Step("bad","label","control_id","$n.missing")});
        check("forward reference rejected",false,new object[]{Step("bad","node","owner_diagram_id","$later.diagram_id")});
        check("unindexed array rejected",false,new object[]{node(),Step("bad","constant","terminal_id","$n.terminal_ids")});
        check("negative array index rejected",false,new object[]{node(),Step("bad","constant","terminal_id","$n.terminal_ids.-1")});
        check("noncanonical index rejected",false,new object[]{node(),Step("bad","constant","terminal_id","$n.terminal_ids.01")});
        check("overflow index rejected",false,new object[]{node(),Step("bad","constant","terminal_id","$n.terminal_ids.9999999999999999")});
        check("scalar index rejected",false,new object[]{node(),Step("bad","delete","object_id","$n.object_id.0")});
        check("wrong loop subtype rejected",false,new object[]{Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("bad","while_info","loop_id","$loop.for_id")});
        check("nested frames and tunnels accepted",true,new object[]{Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("body","for_info","for_id","$loop.for_id"),Step("n","node","owner_diagram_id","$body.diagram_id"),Step("s","for_shift","for_id","$loop.for_id"),Step("left","left_shift","right_id","$s.right_id"),Step("t","tunnel_terms","tunnel_id","$left.left_ids.0")});
        check("plain array output rejected",false,new object[]{Step("c","case","owner_diagram_id","$root.diagram_id"),Step("names","case_names","case_id","$c.case_id"),Step("bad","delete","object_id","$names.frame_names.0")});
        check("plain boolean output rejected",false,new object[]{Step("w","diagram_wires","diagram_id","$root.diagram_id"),Step("state","wire_broken","wire_id","$w.wire_ids.0"),Step("bad","delete","object_id","$state.is_broken")});
        check("deleted producer rejected",false,new object[]{node(),Step("d","delete","object_id","$n.object_id"),Step("bad","node_terminals","node_id","$n.object_id")});
        check("deleted derived alias rejected",false,new object[]{node(),Step("a","node_terminals","node_id","$n.object_id"),Step("d","delete","object_id","$n.object_id"),Step("bad","constant","terminal_id","$a.terminal_ids.0")});
        check("delete derived alias invalidates origin",false,new object[]{node(),Step("a","node_terminals","node_id","$n.object_id"),Step("d","delete","object_id","$a.terminal_ids.0"),Step("bad","constant","terminal_id","$n.pins.x")});
        check("delete parent invalidates nested descendants",false,new object[]{Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("body","for_info","for_id","$loop.for_id"),Step("n","node","owner_diagram_id","$body.diagram_id"),Step("d","delete","object_id","$loop.for_id"),Step("bad","constant","terminal_id","$n.pins.x")});
        check("unrelated node survives deletion",true,new object[]{node(),Step("other","node","owner_diagram_id","$root.diagram_id"),Step("d","delete","object_id","$n.object_id"),Step("ok","node_terminals","node_id","$other.object_id")});
        check("root deletion rejected",false,new object[]{Step("bad","delete","object_id","$root.diagram_id")});
        check("ordinary node cannot grow",false,new object[]{node(),Step("bad","grow","object_id","$n.object_id")});
        check("growable terminals accepted",true,new object[]{Step("g","growable","owner_diagram_id","$root.diagram_id"),Step("more","grow","object_id","$g.object_id"),Step("c","constant","terminal_id","$more.pins.0")});
        check("unknown inspected object not assumed node",false,new object[]{Step("all","diagram_objects","diagram_id","$root.diagram_id"),Step("bad","node_terminals","node_id","$all.object_ids.0")});
        check("scope inspection deletion invalidates aliases",false,new object[]{node(),Step("all","diagram_objects","diagram_id","$root.diagram_id"),Step("d","delete","object_id","$all.object_ids.0"),Step("bad","node_terminals","node_id","$n.object_id")});
        check("move creates new owner lifetime dependency",false,new object[]{node(),Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("body","for_info","for_id","$loop.for_id"),Step("move","move_node","object_id","$n.object_id","owner_diagram_id","$body.diagram_id"),Step("d","delete","object_id","$loop.for_id"),Step("bad","constant","terminal_id","$n.pins.x")});
        var numeric=Step("bad","constant");Map(numeric["args"])["terminal_id"]=1234;
        check("raw numeric reference rejected",false,new object[]{numeric});
        Func<object[]> caseWire=()=>new object[]{Step("control","control"),Step("c","case","owner_diagram_id","$root.diagram_id"),Step("before","case_info","case_id","$c.case_id"),Step("wire","wire","source_terminal","$control.terminal_id","sink_terminal","$before.pins.0")};
        check("Case wire invalidates earlier frames",false,caseWire().Concat(new object[]{Step("bad","node","owner_diagram_id","$before.frame_ids.0")}).ToArray());
        check("Case wire invalidates earlier terminals",false,caseWire().Concat(new object[]{Step("bad","constant","terminal_id","$before.pins.0")}).ToArray());
        check("Case info can be reacquired after wiring",true,caseWire().Concat(new object[]{Step("fresh","case_info","case_id","$c.case_id"),Step("n","node","owner_diagram_id","$fresh.frame_ids.0")}).ToArray());
        check("Case mutation invalidates frame descendants",false,new object[]{Step("control","control"),Step("c","case","owner_diagram_id","$root.diagram_id"),Step("before","case_info","case_id","$c.case_id"),Step("n","node","owner_diagram_id","$before.frame_ids.0"),Step("wire","wire","source_terminal","$control.terminal_id","sink_terminal","$before.pins.0"),Step("bad","constant","terminal_id","$n.pins.0")});
        foreach(string mutation in new[]{"case_frame","set_case_names"}) {
            check(mutation+" invalidates old Case frame aliases",false,new object[]{Step("c","case","owner_diagram_id","$root.diagram_id"),Step("old","case_info","case_id","$c.case_id"),Step("mutation",mutation,"case_id","$c.case_id"),Step("bad","node","owner_diagram_id","$old.frame_ids.0")});
            check(mutation+" permits fresh Case aliases",true,new object[]{Step("c","case","owner_diagram_id","$root.diagram_id"),Step("old","case_info","case_id","$c.case_id"),Step("mutation",mutation,"case_id","$c.case_id"),Step("fresh","case_info","case_id","$c.case_id"),Step("node","node","owner_diagram_id","$fresh.frame_ids.0")});
        }
        check("control direction invalidates old terminal",false,new object[]{Step("c","control"),Step("set","set_indicator","control_id","$c.object_id"),Step("bad","constant","terminal_id","$c.terminal_id")});
        check("control survives direction mutation",true,new object[]{Step("c","control"),Step("set","set_indicator","control_id","$c.object_id"),Step("read","is_indicator","control_id","$c.object_id")});
        check("terminal can be reacquired after direction mutation",true,new object[]{Step("c","control"),Step("set","set_indicator","control_id","$c.object_id"),Step("fresh","control_terminal","control_id","$c.object_id"),Step("ok","constant","terminal_id","$fresh.terminal_id")});
        check("reacquired terminal invalidated by later direction mutation",false,new object[]{Step("c","control"),Step("fresh","control_terminal","control_id","$c.object_id"),Step("set","set_indicator","control_id","$c.object_id"),Step("bad","constant","terminal_id","$fresh.terminal_id")});
        check("indicator flag is data and not a native handle",false,new object[]{Step("c","control"),Step("read","is_indicator","control_id","$c.object_id"),Step("bad","delete","object_id","$read.is_indicator")});
        Func<object[]> movedArray=()=>{var array=Step("array","control");Map(array["args"])["style"]="Array";return new object[]{Step("source","control"),Step("rep","representation","numeric_id","$source.object_id"),Step("direction","set_indicator","control_id","$source.object_id"),array,Step("move","array_move_element","control_id","$source.object_id","owner_id","$array.object_id")};};
        check("Numeric source representation/direction may precede Array move",true,movedArray().Concat(new object[]{Step("info","array_info","array_id","$array.object_id")}).ToArray());
        check("Array move invalidates original source terminal",false,movedArray().Concat(new object[]{Step("bad","constant","terminal_id","$source.terminal_id")}).ToArray());
        check("Array direction inheritance invalidates original Array terminal",false,movedArray().Concat(new object[]{Step("bad","constant","terminal_id","$array.terminal_id")}).ToArray());
        check("Array move returns a fresh terminal for typed creation",true,movedArray().Concat(new object[]{Step("fresh","typed_control","terminal_id","$move.terminal_id")}).ToArray());
        check("direction through inspected control invalidates uncertain terminal aliases",false,new object[]{Step("c","control"),Step("all","connector_controls"),Step("set","set_indicator","control_id","$all.control_ids.0"),Step("bad","constant","terminal_id","$c.terminal_id")});
        foreach(string creation in new[]{"typed_control","typed_indicator"}) {
            check(creation+" survives its direction mutation",true,new object[]{node(),Step("c",creation,"terminal_id","$n.pins.x"),Step("set","set_indicator","control_id","$c.control_id"),Step("read","is_indicator","control_id","$c.control_id"),Step("fresh","control_terminal","control_id","$c.control_id")});
            check(creation+" direction does not invalidate upstream node terminal",true,new object[]{node(),Step("c",creation,"terminal_id","$n.pins.x"),Step("set","set_indicator","control_id","$c.control_id"),Step("use","constant","terminal_id","$n.pins.y")});
            check(creation+" direction invalidates its original terminal",false,new object[]{node(),Step("c",creation,"terminal_id","$n.pins.x"),Step("set","set_indicator","control_id","$c.control_id"),Step("bad","constant","terminal_id","$c.created_terminal_id")});
        }
        check("created control direction invalidates inspected terminal alias",false,new object[]{Step("c","control"),Step("f","find_control"),Step("set","set_indicator","control_id","$c.object_id"),Step("bad","constant","terminal_id","$f.terminal_id")});
        check("inspected control direction invalidates original terminal",false,new object[]{Step("c","control"),Step("f","find_control"),Step("set","set_indicator","control_id","$f.control_id"),Step("bad","constant","terminal_id","$c.terminal_id")});
        check("deleting created control invalidates inspected alias",false,new object[]{Step("c","control"),Step("f","find_control"),Step("del","delete","object_id","$c.object_id"),Step("bad","label","control_id","$f.control_id")});
        check("deleting inspected control invalidates original alias",false,new object[]{Step("c","control"),Step("f","find_control"),Step("del","delete","object_id","$f.control_id"),Step("bad","label","control_id","$c.object_id")});
        check("fresh inspection allowed after deleting inspected object",true,new object[]{Step("c","control"),Step("f","find_control"),Step("del","delete","object_id","$f.control_id"),Step("fresh","find_control"),Step("ok","label","control_id","$fresh.control_id")});
        foreach(string mutation in new[]{"property_class","property_select","invoke_class","invoke_select"}) {
            string field=mutation.StartsWith("property",StringComparison.Ordinal)?"property_id":"invoke_id";
            check(mutation+" invalidates earlier terminals",false,new object[]{node(),Step("old","node_terminals","node_id","$n.object_id"),Step("mut",mutation,field,"$n.object_id"),Step("bad","constant","terminal_id","$old.pins.0")});
            check(mutation+" allows fresh terminals on surviving node",true,new object[]{node(),Step("mut",mutation,field,"$n.object_id"),Step("fresh","node_terminals","node_id","$n.object_id"),Step("ok","constant","terminal_id","$fresh.pins.0")});
        }
        foreach(string mutation in new[]{"property_item_select","property_item_write"}) {
            check(mutation+" invalidates items and node terminals",false,new object[]{node(),Step("info","property_info","property_id","$n.object_id"),Step("mut",mutation,"property_item_id","$info.property_item_ids.0"),Step("bad","property_item_info","property_item_id","$info.property_item_ids.0")});
            check(mutation+" permits reacquisition from node",true,new object[]{node(),Step("info","property_info","property_id","$n.object_id"),Step("mut",mutation,"property_item_id","$info.property_item_ids.0"),Step("fresh","property_info","property_id","$n.object_id"),Step("ok","property_item_info","property_item_id","$fresh.property_item_ids.0")});
        }
        check("property metadata strings are not control refs",false,new object[]{node(),Step("info","property_info","property_id","$n.object_id"),Step("bad","control_reference","control_id","$info.supported_properties.0")});
        check("node cannot be a generic control reference source",false,new object[]{node(),Step("bad","control_reference","control_id","$n.object_id")});
        check("cluster child supports a bound control reference",true,new object[]{Step("cluster","control"),Step("child","cluster_child","owner_id","$cluster.object_id"),Step("ref","control_reference","control_id","$child.object_id"),Step("pins","node_terminals","node_id","$ref.reference_constant_id")});
        check("deleted cluster invalidates child bound reference",false,new object[]{Step("cluster","control"),Step("child","cluster_child","owner_id","$cluster.object_id"),Step("ref","control_reference","control_id","$child.object_id"),Step("del","delete","object_id","$cluster.object_id"),Step("bad","node_terminals","node_id","$ref.reference_constant_id")});
        check("strict reference wire invalidates property items",false,new object[]{Step("ctl","control"),Step("ref","control_reference","control_id","$ctl.object_id"),Step("refpins","node_terminals","node_id","$ref.reference_constant_id"),node(),Step("info","property_info","property_id","$n.object_id"),Step("w","wire","source_terminal","$refpins.pins.0","sink_terminal","$n.pins.0"),Step("bad","property_item_info","property_item_id","$info.property_item_ids.0")});
        check("bound reference node deletion preserves original control",true,new object[]{Step("ctl","control"),Step("ref","control_reference","control_id","$ctl.object_id"),Step("d","delete","object_id","$ref.reference_constant_id"),Step("ok","label","control_id","$ctl.object_id")});
        check("control deletion invalidates bound reference node",false,new object[]{Step("ctl","control"),Step("ref","control_reference","control_id","$ctl.object_id"),Step("d","delete","object_id","$ctl.object_id"),Step("bad","node_terminals","node_id","$ref.reference_constant_id")});
        check("Invoke metadata is not a reference",false,new object[]{node(),Step("info","invoke_info","invoke_id","$n.object_id"),Step("bad","delete","object_id","$info.method.0")});
        check("ordinary control cannot be a PropertyItem",false,new object[]{Step("ctl","control"),Step("bad","property_item_info","property_item_id","$ctl.object_id")});
        foreach(string mutation in new[]{"formula_expression","formula_input","formula_output"}) {
            check(mutation+" invalidates earlier parameter aliases",false,new object[]{node(),Step("info","formula_info","formula_id","$n.object_id"),Step("mut",mutation,"formula_id","$n.object_id"),Step("bad","formula_parameter_info","parameter_id","$info.parameter_ids.0")});
            check(mutation+" allows fresh parameter inspection",true,new object[]{node(),Step("info","formula_info","formula_id","$n.object_id"),Step("mut",mutation,"formula_id","$n.object_id"),Step("fresh","formula_info","formula_id","$n.object_id"),Step("ok","formula_parameter_info","parameter_id","$fresh.parameter_ids.0")});
        }
        foreach(string mutation in new[]{"formula_parameter_name","formula_parameter_delete"}) {
            check(mutation+" invalidates parameter terminals",false,new object[]{node(),Step("info","formula_info","formula_id","$n.object_id"),Step("param","formula_parameter_info","parameter_id","$info.parameter_ids.0"),Step("mut",mutation,"parameter_id","$info.parameter_ids.0"),Step("bad","constant","terminal_id","$param.terminal_id")});
            check(mutation+" preserves Formula node for reacquisition",true,new object[]{node(),Step("info","formula_info","formula_id","$n.object_id"),Step("mut",mutation,"parameter_id","$info.parameter_ids.0"),Step("fresh","formula_info","formula_id","$n.object_id")});
        }
        check("new Formula parameter remains live after creation",true,new object[]{node(),Step("add","formula_input","formula_id","$n.object_id"),Step("inspect","formula_parameter_info","parameter_id","$add.parameter_id")});
        check("Formula wire invalidates old parameters",false,new object[]{node(),Step("info","formula_info","formula_id","$n.object_id"),Step("param","formula_parameter_info","parameter_id","$info.parameter_ids.0"),Step("c","control"),Step("w","wire","source_terminal","$c.terminal_id","sink_terminal","$param.terminal_id"),Step("bad","formula_parameter_info","parameter_id","$info.parameter_ids.0")});
        check("ordinary node cannot be Formula parameter",false,new object[]{node(),Step("bad","formula_parameter_info","parameter_id","$n.object_id")});
        Action<string,object[],string[]> diagrams=(name,steps,expected)=> {
            string error=null;bool passed=false;
            try {var actual=LiveDiagramReferences(new Dictionary<string,object>{{"steps",steps}},catalog);passed=new HashSet<string>(actual).SetEquals(expected);if(!passed)error="Expected "+String.Join(",",expected)+"; got "+String.Join(",",actual);}
            catch(ArgumentException ex){error=ex.Message;}
            if(!passed)failures++;results.Add(new{name,passed,error});
        };
        diagrams("deleted diagrams omitted from automatic audit",new object[]{Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("body","for_info","for_id","$loop.for_id"),Step("d","delete","object_id","$loop.for_id")},new string[0]);
        diagrams("live inner diagram retained without root",new object[]{Step("loop","for","owner_diagram_id","$root.diagram_id"),Step("body","for_info","for_id","$loop.for_id")},new[]{"$body.diagram_id"});
        diagrams("Case audit retains only reacquired frame array",caseWire().Concat(new object[]{Step("fresh","case_info","case_id","$c.case_id")}).ToArray(),new[]{"$fresh.frame_ids"});
        return new{passed=failures==0,count=results.Count,failures,cases=results,labview_executed=false};
    }
}
}
