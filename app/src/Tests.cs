using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OwnLabView {
public static class Tests {
    static List<object> results=new List<object>();
    static void Check(string name,Action action) { try{action();results.Add(new{name,passed=true});}catch(Exception ex){results.Add(new{name,passed=false,error=Data.Error(ex)});} }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action){bool failed=false;try{action();}catch(ArgumentException){failed=true;}catch(ArithmeticException){failed=true;}Assert(failed,"Invalid input was accepted");}
    static object Sample(){return Data.Json.DeserializeObject(Service.Sample);}
    static Dictionary<string,object> Decode(object value){return Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(value)));}
    public static int Run() {
        results.Clear();object referenceReport=null;
        Check("correct arithmetic with overrides",()=>{var g=Graph.Parse(Sample());var r=Decode(g.Evaluate(new Dictionary<string,object>{{"a",-4.5},{"b",12}}));Assert(Convert.ToDouble(Data.Map(r["outputs"])["result"])==7.5,"Incorrect sum");Assert(!(bool)r["labview_executed"],"Evaluation misreported as LabVIEW");});
        Check("duplicate node rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("\"id\":\"b\"","\"id\":\"a\"")))));
        Check("output path traversal rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("AdditionDemo","../unsafe")))));
        Check("reserved Windows name rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("AdditionDemo","CON")))));
        Check("unknown action rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("\"add\"","\"shell\"")))));
        Check("unknown fields rejected",()=>{var g=Data.Map(Sample());g["run_command"]="anything";Reject(()=>Graph.Parse(g));});
        Check("boolean is not a numeric constant",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("\"value\":2","\"value\":true")))));
        Check("multiple input drivers rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("sum.b","sum.a")))));
        Check("unknown destination port rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("sum.b","sum.c")))));
        Check("unwired input rejected",()=>{var g=Data.Map(Sample());g["wires"]=Data.Array(g["wires"]).Skip(1).ToArray();Reject(()=>Graph.Parse(g));});
        Check("cycle rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("a.out","sum.out")))));
        Check("division zero rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("\"add\"","\"divide\""))).Evaluate(new Dictionary<string,object>{{"b",0}})));
        Check("unknown input override rejected",()=>Reject(()=>Graph.Parse(Sample()).Evaluate(new Dictionary<string,object>{{"sum",4}})));
        Check("non finite arithmetic rejected",()=>Reject(()=>Graph.Parse(Data.Json.DeserializeObject(Service.Sample.Replace("\"add\"","\"multiply\""))).Evaluate(new Dictionary<string,object>{{"a",1e308},{"b",1e308}})));
        Check("graph size limited",()=>{var g=Data.Map(Sample());g["nodes"]=Enumerable.Repeat(Data.Array(g["nodes"])[0],201).ToArray();Reject(()=>Graph.Parse(g));});
        Check("RPC malformed JSON responds parse error",()=>{var r=Decode(new Protocol().Handle("{"));Assert(Convert.ToInt32(Data.Map(r["error"])["code"])==-32700,"Missing parse error");});
        Check("RPC scalar responds invalid request",()=>{var r=Decode(new Protocol().Handle("3"));Assert(Convert.ToInt32(Data.Map(r["error"])["code"])==-32600,"Missing invalid-request error");});
        Check("RPC handshake required",()=>{var r=Decode(new Protocol().Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));Assert(Convert.ToInt32(Data.Map(r["error"])["code"])==-32002,"Handshake bypassed");});
        Check("RPC version negotiated, notification silent, tools available",()=>{var p=new Protocol();var r=Decode(p.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2099-01-01\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}"));Assert((string)Data.Map(r["result"])["protocolVersion"]=="2025-06-18","Unsupported version echoed");Assert(p.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")==null,"Notification response emitted");Assert(Data.Array(Data.Map(Decode(p.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))["result"])["tools"]).Length>=6,"Missing tools");});
        Check("service ignores no unknown argument",()=>Reject(()=>Service.Call("labview_status",new Dictionary<string,object>{{"execute",true}})));
        Check("no legacy runtime dependencies",()=>{var references=typeof(Service).Assembly.GetReferencedAssemblies();Assert(references.All(x=>x.Name=="mscorlib"||x.Name.StartsWith("System")||x.Name=="Microsoft.CSharp"),"Non-system assembly reference");Assert(!Data.Json.Serialize(Service.Capabilities()).Contains("vendor_runtime_required\":true"),"Vendor runtime declared");});
        Check("recipe reference type and lifetime regression",()=>{var catalog=Data.Map(Data.Map(Decode(Recipe.Catalog()))["operations"]);var guardReport=Decode(RecipeReferenceGuard.SelfTest(catalog));referenceReport=guardReport;Assert((bool)guardReport["passed"],Data.Json.Serialize(guardReport));});
        Check("shipped recipe examples validate offline",()=>{foreach(string name in new[]{"multiply","for-loop","case","for-conditional","indicator-direction","subvi-function","bound-control-read-reset","formula-square"})Recipe.Validate(Data.Json.DeserializeObject(System.IO.File.ReadAllText(System.IO.Path.Combine(Storage.Root,"recipes",name+".json"))));});
        Check("grow resources bounded before NI",()=>Reject(()=>Recipe.Validate(Data.Json.DeserializeObject("{\"schema_version\":1,\"name\":\"TooManyChunks\",\"steps\":[{\"id\":\"a\",\"op\":\"growable\",\"args\":{\"owner_diagram_id\":\"$root.diagram_id\",\"style\":\"Build Array\",\"x\":50,\"y\":50}},{\"id\":\"b\",\"op\":\"grow\",\"args\":{\"object_id\":\"$a.object_id\",\"chunks\":65}}]}"))));
        Check("duplicate terminal names never select an arbitrary handle",()=>{var p=Recipe.PinAliases(new[]{"reference","Method","Method","0","Value"},new uint[]{10,20,30,40,50});Assert(!p.ContainsKey("Method"),"Duplicate name retained");Assert((uint)p["1"]==20&&(uint)p["2"]==30&&(uint)p["0"]==10&&(uint)p["Value"]==50,"Raw pin order lost");});
        int failures=results.Count(r=>!(bool)r.GetType().GetProperty("passed").GetValue(r,null));
        var report=new{passed=failures==0,total=results.Count,failures,tests=results,reference_guard=referenceReport};
        Console.WriteLine(Data.Json.Serialize(report));return failures==0?0:1;
    }
}
}
