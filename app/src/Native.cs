using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OwnLabView {
public static class Native {
    static readonly string[] RequiredHelpers={"Native_Create","Native_AddNode_v2","Native_AddControl_v2","Native_Wire","Native_GetTerminalName"};
    public static bool Available { get { return RequiredHelpers.All(n=>File.Exists(Path.Combine(Storage.Root,"helpers",n+".vi"))) && File.Exists(Path.Combine(Storage.Root,"helpers","provenance.json")); } }
    public static object Build(Graph graph,bool verify) {
        if(graph.Nodes.Values.Any(n=>n.Kind=="constant"))throw new ArgumentException("Native generation of constants is not yet verified. Use input nodes or offline plan evaluation.");
        if(!Available)throw new InvalidOperationException("Audited NI-only helper set is unavailable. No VI created.");
        // Check finite scalar execution before creating any files.
        graph.Evaluate(new Dictionary<string,object>());
        NiApplicationLifetime.EnsureForBuild();
        string folder=Storage.NewProject(graph),request=Path.Combine(folder,"graph.json"),report=Path.Combine(folder,"build-report.json");
        Storage.WriteNew(request,graph.Source);
        string executable=Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe");
        var start=new ProcessStartInfo(executable,"--build-worker \""+request+"\" "+(verify?"verify":"create")) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Storage.Root,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        var output=new StringBuilder();var errors=new StringBuilder();
        using(var process=new Process{StartInfo=start}) {
            process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)lock(output)output.AppendLine(e.Data);};
            process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};
            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
            if(!process.WaitForExit(180000)) {process.Kill();process.WaitForExit();throw new TimeoutException("Build worker timed out. NI LabVIEW is left running; inspect any modal dialog. Partial project retained: "+folder);}
            process.WaitForExit();
            if(process.ExitCode!=0)throw new InvalidOperationException("Build failed; partial project retained at "+folder+". "+errors+output);
        }
        if(!File.Exists(report))throw new IOException("Worker exited without a build report.");
        var result=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(report,Encoding.UTF8)));
        if(!result.ContainsKey("created") || !Object.Equals(result["created"],true) || !File.Exists(Data.Text(result,"vi_path")))throw new IOException("Worker report did not verify VI output.");
        return result;
    }
    public static object Worker(string path,bool verify) {
        path=Path.GetFullPath(path);Storage.NoReparse(path);
        if(!path.StartsWith(Path.Combine(Storage.Root,"projects")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path)!="graph.json")throw new UnauthorizedAccessException("Worker accepts only newly saved application projects.");
        var graph=Graph.Parse(Data.Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8)));
        if(graph.Nodes.Values.Any(n=>n.Kind=="constant"))throw new ArgumentException("Native constants not implemented.");
        var expected=Data.Map(Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(graph.Evaluate(new Dictionary<string,object>()))))["outputs"]);
        string folder=Path.GetDirectoryName(path),targetPath=Path.Combine(folder,Path.GetFileName(Path.GetDirectoryName(path))+".vi");
        if(File.Exists(targetPath))throw new IOException("VI output already exists; never overwrite.");
        var provenance=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Path.Combine(Storage.Root,"helpers","provenance.json"),Encoding.UTF8)));
        var helpersMetadata=Data.Map(provenance["helpers"]);
        foreach(string name in RequiredHelpers) {
            string helperPath=Path.Combine(Storage.Root,"helpers",name+".vi");
            string expectedHash=Data.Text(Data.Map(helpersMetadata[name]),"sha256");
            if(Data.Hash(helperPath)!=expectedHash)throw new IOException("Helper hash mismatch: "+name);
        }
        object app=null,target=null;var references=new List<object>();var helpers=new Dictionary<string,object>();
        var pins=new Dictionary<string,uint>();var pinNames=new Dictionary<string,object>();var actual=new Dictionary<string,double>();var loadedHelperPaths=new Dictionary<string,string>();
        var lease=new NiSessionLease();
        try {
            // Build is the explicit action that may start NI LabVIEW. Status never starts it.
            app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));
            lease.Attached();
            foreach(string name in RequiredHelpers) {
                object helper=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,Path.Combine(Storage.Root,"helpers",name+".vi"),"",false,0);
                references.Add(helper);helpers[name]=helper;
                loadedHelperPaths[name]=LabView.VerifyHelperPath(helper,Data.Text(Data.Map(helpersMetadata[name]),"sha256"));
                var callees=(IEnumerable)LabView.Invoke(helper,"_CalleesPaths",BindingFlags.GetProperty);
                foreach(object callee in callees)if(!String.IsNullOrWhiteSpace(Convert.ToString(callee)))throw new InvalidOperationException("Helper has an unaudited callee; refusing execution: "+name+" -> "+callee);
            }
            Helper(helpers["Native_Create"],new Dictionary<string,object>{{"output_path",targetPath}});
            if(!File.Exists(targetPath))throw new IOException("Create helper did not save VI.");
            target=LabView.Invoke(app,"GetVIReference",BindingFlags.InvokeMethod,targetPath,"",false,0);references.Add(target);
            if(!String.Equals(Path.GetFullPath(Convert.ToString(LabView.Invoke(target,"Path",BindingFlags.GetProperty))),targetPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("LabVIEW returned a different VI for the new output path");
            var levelRows=new Dictionary<int,int>();int panelRow=0;
            foreach(Node node in graph.Order) {
                int row=levelRows.ContainsKey(node.Level)?levelRows[node.Level]:0;levelRows[node.Level]=row+1;
                uint style=node.Kind=="input"?3000u:node.Kind=="indicator"?3001u:node.Kind=="add"?1050u:node.Kind=="subtract"?3916u:node.Kind=="multiply"?3917u:3918u;
                object helper=helpers[node.IsMath?"Native_AddNode_v2":"Native_AddControl_v2"];
                Helper(helper,new Dictionary<string,object>{{"target_path",targetPath},{"style",style},{"x",(short)(node.IsMath?140+node.Level*180:40+(panelRow%3)*170)},{"y",(short)(node.IsMath?70+row*120:40+(panelRow/3)*80)},{"label",node.Label}});
                if(node.IsMath) {
                    var terminalNames=new Dictionary<string,uint>(StringComparer.Ordinal);
                    foreach(object id in (IEnumerable)Get(helper,"terminal_ids")) {
                        uint terminal=Convert.ToUInt32(id);Helper(helpers["Native_GetTerminalName"],new Dictionary<string,object>{{"terminal_id",terminal}});
                        terminalNames.Add(Convert.ToString(Get(helpers["Native_GetTerminalName"],"terminal_name")),terminal);
                    }
                    string outputName=node.Kind=="add"?"x+y":node.Kind=="subtract"?"x-y":node.Kind=="multiply"?"x*y":"x/y";
                    if(!terminalNames.ContainsKey("x")||!terminalNames.ContainsKey("y")||!terminalNames.ContainsKey(outputName))throw new InvalidOperationException("Unexpected native terminal signature for "+node.Kind+": "+Data.Json.Serialize(terminalNames));
                    pins[node.Id+".a"]=terminalNames["x"];pins[node.Id+".b"]=terminalNames["y"];pins[node.Id+".out"]=terminalNames[outputName];pinNames[node.Id]=terminalNames;
                } else {pins[node.Id+(node.Kind=="input"?".out":".in")]=Convert.ToUInt32(Get(helper,"terminal_id"));panelRow++;}
            }
            foreach(Wire wire in graph.Wires)Helper(helpers["Native_Wire"],new Dictionary<string,object>{{"source_terminal",pins[wire.From]},{"sink_terminal",pins[wire.To]}});
            foreach(Node node in graph.Nodes.Values.Where(n=>n.Kind=="input"))LabView.Invoke(target,"SetControlValue",BindingFlags.InvokeMethod,node.Label,node.Value);
            LabView.Invoke(target,"MakeCurValueDefault",BindingFlags.InvokeMethod);
            LabView.Invoke(target,"SaveInstrument",BindingFlags.InvokeMethod,targetPath,false,false);
            if(verify) {
                LabView.Invoke(target,"Run",BindingFlags.InvokeMethod,false);
                foreach(Node node in graph.Nodes.Values.Where(n=>n.Kind=="indicator")) {
                    double value=Data.Number(Get(target,node.Label)),reference=Data.Number(expected[node.Id]);
                    if(Math.Abs(value-reference)>1e-9*Math.Max(1,Math.Abs(reference)))throw new InvalidOperationException("LabVIEW output mismatch at "+node.Id+": "+value+" vs "+reference);
                    actual[node.Id]=value;
                }
            }
            string html=Path.Combine(folder,"documentation");Directory.CreateDirectory(html);
            LabView.Invoke(target,"PrintVIToHTML",BindingFlags.InvokeMethod,Path.Combine(html,"VI.html"),false,4,0,24,html);
            var report=new {created=true,vi_path=targetPath,vi_sha256=Data.Hash(targetPath),labview_version=Convert.ToString(LabView.Invoke(app,"Version",BindingFlags.GetProperty)),labview_executed=verify,execution_verified=verify,execution_state=LabView.Invoke(target,"ExecState",BindingFlags.GetProperty),actual,expected,terminal_names=pinNames,helper_callees=new string[0],loaded_helper_paths=loadedHelperPaths,helper_hashes=helpersMetadata,runtime="Windows .NET Framework + NI LabVIEW + five user-authored native NI helper VIs",helper_provenance="User-authored helper VIs were originally produced with licensed vendor authoring tools. Those tools are not loaded or called by this app. Independent helper rebuild chain remains pending.",documentation=Path.Combine(html,"VI.html"),complete_replacement=false};
            Storage.WriteNew(Path.Combine(folder,"build-report.json"),report);return report;
        } catch(Exception ex) {
            string failed=Path.Combine(folder,"build-failure.json");if(!File.Exists(failed))Storage.WriteNew(failed,new{created=false,error=Data.Error(ex),partial_vi_exists=File.Exists(targetPath),path=targetPath});throw;
        } finally {for(int i=references.Count-1;i>=0;i--)LabView.Release(references[i]);LabView.Release(app);lease.Dispose();}
    }
    static object Get(object vi,string name){return LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,name);}
    static void Helper(object helper,Dictionary<string,object> args) {
        foreach(var arg in args)LabView.Invoke(helper,"SetControlValue",BindingFlags.InvokeMethod,arg.Key,arg.Value);
        LabView.Invoke(helper,"Run",BindingFlags.InvokeMethod,false);
        var error=(IList)Get(helper,"error out");
        if(Convert.ToBoolean(error[0]))throw new InvalidOperationException("Native helper error "+error[1]+": "+error[2]);
    }
}
}
