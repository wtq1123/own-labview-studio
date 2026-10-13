using System;using System.Collections;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using OwnLabView;
class RunClassSuite {
 static Dictionary<string,object> Read(string p){return Data.Map(Data.Json.DeserializeObject(File.ReadAllText(p)));}
 static uint ClassInput(ClassGraph g,Dictionary<string,uint> pins,string field){return pins.Where(p=>p.Key!=field&&p.Key.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0&&!Convert.ToBoolean(g.N.Call("Own20261012_GetTerminalContext",ClassGraph.D("terminal_id",p.Value),"is_source")["is_source"])).Select(p=>p.Value).Single();}
 [STAThread]static int Main(string[] a){typeof(Storage).GetField("Root").SetValue(null,a[0]);object app=null,project=null,context=null;var refs=new List<object>();var checks=new List<object>();string phase="attach";try{
  var prep=Read(Path.Combine(a[1],"prepared.json"));string grand=Data.Text(prep,"vi_path"),dir=Path.GetDirectoryName(grand),child=Directory.GetFiles(Path.Combine(dir,"p"),"*.lvclass").Single(),root=Directory.GetFiles(Path.Combine(dir,@"p\p"),"*.lvclass").Single();
  var hashes=Directory.GetFiles(dir,"*",SearchOption.AllDirectories).Where(p=>new[]{".lvclass",".vi"}.Contains(Path.GetExtension(p))).ToDictionary(p=>p,Data.Hash);
  using(var lease=new NiSessionLease()){app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();project=LabView.Invoke(app,"NewProject",BindingFlags.InvokeMethod);context=LabView.Invoke(project,"Application",BindingFlags.GetProperty);
   using(var n=new TypeImport.Session(context,refs,ClassSnapshot.Accepted(grand))){
    object caller;string path;var expected=new Dictionary<string,object>();
    if(a[2]=="create"){
     phase="finite caller authoring";var g=new ClassGraph(n,Path.Combine(a[1],"OopFinite_"+Guid.NewGuid().ToString("N").Substring(0,8)+".vi"),a[1]);path=g.Path;caller=g.VI;
     foreach(string field in new[]{"Gain","Enabled","Title"}){
      string method=Directory.GetFiles(Path.GetDirectoryName(root),"Read_"+field+"_*.vi").Single();int ix=0;
      foreach(string cls in new[]{root,child,grand}){
       var constant=n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",cls,"x",(short)40,"y",(short)(150+ix*100)),"terminal_id");var pins=g.Pins(g.Sub(method));g.Wire(Convert.ToUInt32(constant["terminal_id"]),ClassInput(g,pins,field));string label=field+"_"+ix;g.Indicator(pins[field],label);expected[label]=field=="Gain"?(object)new double[]{2.5,42,-7.25}[ix]:field=="Enabled"?(object)new bool[]{true,false,true}[ix]:new string[]{"自有类","子类","孙类"}[ix];ix++;
      }
     }
     var rootReport=Read(Path.Combine(a[1],"base.json"));
     foreach(string field in new[]{"Gain","Enabled","Title","Count"}){
      string write=Directory.GetFiles(Path.GetDirectoryName(root),"Write_"+field+"_*.vi").Single(),read=Directory.GetFiles(Path.GetDirectoryName(root),"Read_"+field+"_*.vi").Single();var w=g.Pins(g.Sub(write));var cls=n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",root,"x",(short)40,"y",(short)500),"terminal_id");g.Wire(Convert.ToUInt32(cls["terminal_id"]),ClassInput(g,w,field));g.Control(w[field],"Set_"+field);var r=g.Pins(g.Sub(read));uint output=w.Where(p=>p.Key.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0&&Convert.ToBoolean(n.Call("Own20261012_GetTerminalContext",ClassGraph.D("terminal_id",p.Value),"is_source")["is_source"])).Select(p=>p.Value).Single();g.Wire(output,ClassInput(g,r,field));g.Indicator(r[field],"Roundtrip_"+field);expected["Roundtrip_"+field]=field=="Gain"?(object)-0.5:field=="Enabled"?(object)false:field=="Title"?(object)"读写验收":"unused";if(field=="Count")expected["Roundtrip_"+field]=19.0;
     }
     int derivedIndex=0;
     foreach(string clsPath in new[]{root,child,grand}){
      var cls=n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",clsPath,"x",(short)40,"y",(short)700),"terminal_id");var write=g.Pins(g.Sub(Directory.GetFiles(Path.GetDirectoryName(root),"Write_Count_*.vi").Single()));g.Wire(Convert.ToUInt32(cls["terminal_id"]),ClassInput(g,write,"Count"));g.Control(write["Count"],"SetInheritedCount_"+derivedIndex);
      uint obj=write.Where(p=>p.Key.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0&&Convert.ToBoolean(n.Call("Own20261012_GetTerminalContext",ClassGraph.D("terminal_id",p.Value),"is_source")["is_source"])).Select(p=>p.Value).Single();
      foreach(string field in new[]{"Count","Gain"}){var read=g.Pins(g.Sub(Directory.GetFiles(Path.GetDirectoryName(root),"Read_"+field+"_*.vi").Single()));g.Wire(obj,ClassInput(g,read,field));string label="Inherited_"+field+"_"+derivedIndex;g.Indicator(read[field],label);expected[label]=field=="Count"?99.0:new double[]{2.5,42,-7.25}[derivedIndex];}derivedIndex++;
     }
     derivedIndex=0;
     foreach(string clsPath in new[]{child,grand}){
      var cls=n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",clsPath,"x",(short)40,"y",(short)800),"terminal_id");var write=g.Pins(g.Sub(Directory.GetFiles(Path.GetDirectoryName(child),"Write_Offset_*.vi").Single()));g.Wire(Convert.ToUInt32(cls["terminal_id"]),ClassInput(g,write,"Offset"));g.Control(write["Offset"],"SetOffset_"+derivedIndex);
      uint obj=write.Where(p=>p.Key.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0&&Convert.ToBoolean(n.Call("Own20261012_GetTerminalContext",ClassGraph.D("terminal_id",p.Value),"is_source")["is_source"])).Select(p=>p.Value).Single();
      foreach(string field in new[]{"Offset","Gain"}){string method=Directory.GetFiles(field=="Offset"?Path.GetDirectoryName(child):Path.GetDirectoryName(root),"Read_"+field+"_*.vi").Single();var read=g.Pins(g.Sub(method));g.Wire(obj,ClassInput(g,read,field));string label="ChildWrite_"+field+"_"+derivedIndex;g.Indicator(read[field],label);expected[label]=field=="Offset"?5.25:new double[]{42,-7.25}[derivedIndex];}derivedIndex++;
     }
     g.Save();Storage.WriteNew(Path.Combine(a[1],"finite-caller.json"),new{path,sha256=Data.Hash(path),expected,parent_method_reused=true,only_new_owned_finite_caller=true});
    }else{var saved=Read(Path.Combine(a[1],"finite-caller.json"));path=Data.Text(saved,"path");if(Data.Hash(path)!=Data.Text(saved,"sha256"))throw new IOException("Finite caller changed.");expected=Data.Map(saved["expected"]);caller=n.Open(path);}
    phase="finite runtime";if(n.Type(caller)!=1||Convert.ToInt32(LabView.Invoke(caller,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Caller not compiled.");
    LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"Set_Gain",-0.5);LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"Set_Enabled",false);LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"Set_Title","读写验收");LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"Set_Count",19.0);
    for(int i=0;i<3;i++)LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"SetInheritedCount_"+i,99.0);
    for(int i=0;i<2;i++)LabView.Invoke(caller,"SetControlValue",BindingFlags.InvokeMethod,"SetOffset_"+i,5.25);
    LabView.Invoke(caller,"Run",BindingFlags.InvokeMethod,false);foreach(var pair in expected){object actual=LabView.Invoke(caller,"GetControlValue",BindingFlags.InvokeMethod,pair.Key);if(!Recipe.ExactControlValueEqual(Recipe.NormalizeControlDefault(pair.Value,actual),actual))throw new Exception("Wrong result "+pair.Key+": "+Data.Json.Serialize(actual));checks.Add(new{label=pair.Key,value=actual,clr_type=actual.GetType().FullName,passed=true});}
    foreach(var row in hashes)if(Data.Hash(row.Key)!=row.Value)throw new IOException("Prepared class hierarchy changed.");var fresh=n.ClassMetadata(grand);if(!Data.Text(Data.Map(fresh["native_parent"]),"path").Equals(child,StringComparison.OrdinalIgnoreCase))throw new IOException("Fresh parent differs.");
    Storage.WriteNew(Path.Combine(a[1],"runtime-"+a[2]+".json"),new{passed=true,checks,caller_path=path,caller_sha256=Data.Hash(path),class_hashes=hashes,native_class=fresh,source_hierarchy_unchanged=true,window_operations_used=false,same_parent_methods_called=true});
   }
  }return 0;
 }catch(Exception e){Storage.WriteNew(Path.Combine(a[1],"runtime-failure-"+a[2]+".json"),new{phase,error=e.ToString(),checks});return 1;}finally{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);if(project!=null)try{LabView.Invoke(project,"Close",BindingFlags.InvokeMethod);}catch{}LabView.Release(context);LabView.Release(project);LabView.Release(app);}}
}
