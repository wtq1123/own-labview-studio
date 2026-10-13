using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OwnLabView {
public sealed class Protocol {
    bool initialized,ready;
    public object Handle(string line) {
        object raw;
        try { raw=Data.Json.DeserializeObject(line); } catch { return Error(null,-32700,"Parse error"); }
        var request=raw as Dictionary<string,object>;
        if(request==null) return Error(null,-32600,"Invalid Request");
        object id=request.ContainsKey("id")?request["id"]:null;
        bool notification=!request.ContainsKey("id");
        if(id!=null && !(id is string || id is int || id is long || id is decimal || id is double)) return Error(null,-32600,"Invalid request id");
        if(!request.ContainsKey("jsonrpc") || !Object.Equals(request["jsonrpc"],"2.0") || !request.ContainsKey("method") || !(request["method"] is string)) return Error(id,-32600,"Invalid Request");
        string method=(string)request["method"];
        var args=request.ContainsKey("params")?request["params"] as Dictionary<string,object>:new Dictionary<string,object>();
        if(args==null) return notification?null:Error(id,-32602,"params must be an object");
        if(notification) { if(method=="notifications/initialized" && initialized)ready=true;return null; }
        if(method=="ping")return Result(id,new{});
        if(method=="initialize") {
            if(initialized)return Error(id,-32600,"Already initialized");
            string version;
            try { version=Data.Text(args,"protocolVersion"); Data.Map(args["capabilities"]);Data.Map(args["clientInfo"]); }
            catch { return Error(id,-32602,"initialize requires protocolVersion, capabilities and clientInfo"); }
            if(version!="2024-11-05" && version!="2025-03-26" && version!="2025-06-18")version="2025-06-18";
            initialized=true;
            return Result(id,new {protocolVersion=version,capabilities=new{tools=new{listChanged=false}},serverInfo=new{name="own-labview-studio",version=Service.Version},instructions="Independent partial LabVIEW application. Read studio_capabilities first. Plan evaluation is not LabVIEW execution. Vendor parity and legacy deletion gate remain incomplete."});
        }
        if(!ready)return Error(id,-32002,"Initialize and send notifications/initialized first");
        if(method=="tools/list") {
            if(args.Count>0)return Error(id,-32602,"No pagination or parameters are supported");
            return Result(id,new{tools=Service.Tools()});
        }
        if(method=="tools/call") {
            string name; Dictionary<string,object> arguments;
            try { Data.Keys(args,"name","arguments","_meta");name=Data.Text(args,"name");arguments=args.ContainsKey("arguments")?Data.Map(args["arguments"]):new Dictionary<string,object>(); }
            catch(Exception ex){return Error(id,-32602,Data.Error(ex));}
            if(!KnownTool(name))return Error(id,-32602,"Unknown tool: "+name);
            try { return Result(id,new{content=new[]{new{type="text",text=Data.Json.Serialize(Service.Call(name,arguments))}},isError=false}); }
            catch(Exception ex) { return Result(id,new{content=new[]{new{type="text",text=Data.Error(ex)}},isError=true}); }
        }
        return Error(id,-32601,"Method not found: "+method);
    }
    static bool KnownTool(string name) {
        foreach(var tool in Service.Tools()) if((string)tool.GetType().GetProperty("name").GetValue(tool,null)==name)return true;
        return false;
    }
    static object Error(object id,int code,string message){return new{jsonrpc="2.0",id,error=new{code,message}};}
    static object Result(object id,object result){return new{jsonrpc="2.0",id,result};}
    public static void Run() {
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        var protocol=new Protocol(); string line;
        while((line=Console.ReadLine())!=null) {
            if(String.IsNullOrWhiteSpace(line))continue;
            object response=line.Length>1048576?Error(null,-32600,"Message exceeds 1 MiB character limit"):protocol.Handle(line);
            if(response!=null){Console.WriteLine(Data.Json.Serialize(response));Console.Out.Flush();}
        }
    }
}
}
