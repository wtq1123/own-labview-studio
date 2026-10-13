using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using OwnLabView;
class RunComplexClass {
 static Dictionary<string,object> Read(string p){return Data.Map(Data.Json.DeserializeObject(File.ReadAllText(p)));}
 static bool Source(ClassGraph g,uint t){return Convert.ToBoolean(g.N.Call("Own20261012_GetTerminalContext",ClassGraph.D("terminal_id",t),"is_source")["is_source"]);}
 static uint Obj(ClassGraph g,Dictionary<string,uint> pins,string field,bool output){return pins.Where(p=>p.Key!=field&&p.Key.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0&&Source(g,p.Value)==output).Select(p=>p.Value).Single();}
 [STAThread] static int Main(string[] a){typeof(Storage).GetField("Root").SetValue(null,a[0]);object app=null,project=null,context=null;var refs=new List<object>();var checks=new List<object>();string phase="attach";try{
 var prep=Read(Path.Combine(a[1],"prepared.json"));string child=Data.Text(prep,"vi_path"),root=Directory.GetFiles(Path.Combine(Path.GetDirectoryName(child),"p"),"*.lvclass").Single();
 var request=Read(Path.Combine(a[1],"base-request.json"));var fields=Data.Array(request["controls"]).Select(Data.Map).ToArray();
 var bundle=ClassSnapshot.Accepted(child);bundle.VerifySources();
 using(var lease=new NiSessionLease()){app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();project=LabView.Invoke(app,"NewProject",BindingFlags.InvokeMethod);context=LabView.Invoke(project,"Application",BindingFlags.GetProperty);
 using(var n=new TypeImport.Session(context,refs,bundle)){
 var expected=new Dictionary<string,object>();var inputs=new Dictionary<string,object>();object caller;string path;
 if(a[2]=="create"){
  phase="finite caller authoring";var g=new ClassGraph(n,Path.Combine(a[1],"ComplexFinite_"+Guid.NewGuid().ToString("N").Substring(0,8)+".vi"),a[1]);path=g.Path;caller=g.VI;
  foreach(var field in fields){string label=Data.Text(field,"label"),read=Directory.GetFiles(Path.GetDirectoryName(root),"Read_"+label+"_*.vi").Single(),write=Directory.GetFiles(Path.GetDirectoryName(root),"Write_"+label+"_*.vi").Single();int ix=0;
   foreach(string cls in new[]{root,child}){
    uint obj=Convert.ToUInt32(n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",cls,"x",(short)40,"y",(short)80),"terminal_id")["terminal_id"]);
    var direct=g.Pins(g.Sub(read));g.Wire(obj,Obj(g,direct,label,false));string d="Default_"+label+ix;g.Indicator(direct[label],d);expected[d]=ClassArtifacts.FieldDefault(field);
    var w=g.Pins(g.Sub(write));g.Wire(obj,Obj(g,w,label,false));string input="Set_"+label+ix;g.Control(w[label],input);
    object changed=File.Exists(Path.Combine(a[1],"changed-values.json"))?Read(Path.Combine(a[1],"changed-values.json"))[label]:label=="Settings"?(object)new object[]{-8.75,new object[]{false,"嵌套读写",new object[]{0.125,0.875}}}:label=="Samples"?(object)new object[]{-0.5,100.25}:label=="Flags"?(object)new object[]{false,true,false}:label=="Names"?(object)new object[]{"测试","","三"}:new object[]{9.0};
    inputs[input]=changed;var r=g.Pins(g.Sub(read));g.Wire(Obj(g,w,label,true),Obj(g,r,label,false));string output="Roundtrip_"+label+ix;g.Indicator(r[label],output);expected[output]=changed;ix++;
   }
  }
  g.Save();Storage.WriteNew(Path.Combine(a[1],"finite-complex-caller.json"),new{path,sha256=Data.Hash(path),expected,inputs});
 }else{var saved=Read(Path.Combine(a[1],"finite-complex-caller.json"));path=Data.Text(saved,"path");if(Data.Hash(path)!=Data.Text(saved,"sha256"))throw new IOException("Caller changed.");expected=Data.Map(saved["expected"]);inputs=Data.Map(saved["inputs"]);caller=n.Open(path);}
 phase="finite runtime";if(Convert.ToInt32(LabView.Invoke(caller,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Caller is not executable.");
 foreach(var input in inputs){var spec=fields.Single(f=>input.Key.StartsWith("Set_"+Data.Text(f,"label"),StringComparison.Ordinal));object current=LabView.Invoke(caller,"GetControlValue",BindingFlags.InvokeMethod,input.Key);LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,input.Key,TypeArtifacts.NormalizeValue(spec,input.Value,current));}
 LabView.Invoke(caller,"Run",BindingFlags.InvokeMethod,false);
 foreach(var pair in expected){var spec=fields.Single(f=>pair.Key.StartsWith("Default_"+Data.Text(f,"label"),StringComparison.Ordinal)||pair.Key.StartsWith("Roundtrip_"+Data.Text(f,"label"),StringComparison.Ordinal));object actual=LabView.Invoke(caller,"GetControlValue",BindingFlags.InvokeMethod,pair.Key);if(!Recipe.ExactControlValueEqual(TypeArtifacts.NormalizeValue(spec,pair.Value,actual),actual))throw new IOException("Wrong value "+pair.Key);checks.Add(new{label=pair.Key,value=actual,passed=true});}
 bundle.VerifySources();Storage.WriteNew(Path.Combine(a[1],"complex-runtime-"+a[2]+".json"),new{passed=true,checks,caller_path=path,source_files_unchanged=true,window_operations_used=false,only_new_finite_caller_executed=true});
 }}return 0;
 }catch(Exception e){Storage.WriteNew(Path.Combine(a[1],"complex-runtime-failure-"+a[2]+".json"),new{phase,error=e.ToString(),checks});return 1;}finally{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);if(project!=null)try{LabView.Invoke(project,"Close",BindingFlags.InvokeMethod);}catch{}LabView.Release(context);LabView.Release(project);LabView.Release(app);}}
}
