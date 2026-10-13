using System;using System.Collections;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using OwnLabView;
class RunTypedMethods {
 static Dictionary<string,object> Read(string p){return Data.Map(Data.Json.DeserializeObject(File.ReadAllText(p)));}
 [STAThread]static int Main(string[] a){typeof(Storage).GetField("Root").SetValue(null,a[0]);object app=null,project=null,context=null;var refs=new List<object>();var checks=new List<object>();string phase="attach";try{
 string cls=Data.Text(Read(Path.Combine(a[1],"typedprepared.json")),"vi_path");var bundle=ClassSnapshot.Accepted(cls);bundle.VerifySources();
 using(var lease=new NiSessionLease()){app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));lease.Attached();project=LabView.Invoke(app,"NewProject",BindingFlags.InvokeMethod);context=LabView.Invoke(project,"Application",BindingFlags.GetProperty);
 using(var n=new TypeImport.Session(context,refs,bundle)){
 object vi;string path;string[] fields={"Flag","Text","Vector","Matrix"};
 if(a[2]=="create"){
  phase="caller";var g=new ClassGraph(n,Path.Combine(a[1],"TypedDynamic_"+Guid.NewGuid().ToString("N").Substring(0,8)+".vi"),a[1]);vi=g.VI;path=g.Path;int i=0;
  foreach(string method in new[]{"Echo","StaticEcho"}){uint obj=Convert.ToUInt32(n.Call("Native_AddConstantFromTypedef",ClassGraph.D("owner_diagram_id",g.Diagram,"typedef_path",cls,"x",(short)30,"y",(short)40),"terminal_id")["terminal_id"]);var p=g.Pins(g.Sub(Path.Combine(Path.GetDirectoryName(cls),method+".vi")));g.Wire(obj,p["self in"]);foreach(string f in fields){g.Control(p[f],f+i);g.Indicator(p[f+"Echo"],f+"Echo"+i);}i++;}g.Save();Storage.WriteNew(Path.Combine(a[1],"typed-caller.json"),new{path,sha256=Data.Hash(path)});
 }else{var saved=Read(Path.Combine(a[1],"typed-caller.json"));path=Data.Text(saved,"path");if(Data.Hash(path)!=Data.Text(saved,"sha256"))throw new IOException("Caller changed.");vi=n.Open(path);}
 phase="runtime";if(Convert.ToInt32(LabView.Invoke(vi,"ExecState",BindingFlags.GetProperty))!=1)throw new IOException("Typed caller broken.");
 for(int pass=0;pass<2;pass++){
 var values=new Dictionary<string,object>{{"Flag",pass==0},{"Text",pass==0?"自定义方法":""},{"Vector",pass==0?new double[]{1.25,-3,9}:new double[0]},{"Matrix",pass==0?new double[,]{{1.25,2},{-5,9.75}}:new double[,]{{3},{4},{5}}}};
 for(int i=0;i<2;i++)foreach(var value in values)LabView.Invoke(vi,"SetControlValue",BindingFlags.InvokeMethod,value.Key+i,value.Value);LabView.Invoke(vi,"Run",BindingFlags.InvokeMethod,false);
 for(int i=0;i<2;i++)foreach(var value in values){object actual=LabView.Invoke(vi,"GetControlValue",BindingFlags.InvokeMethod,value.Key+"Echo"+i);var array=value.Value as Array;bool ok;if(array==null)ok=Object.Equals(value.Value,actual);else {var other=actual as Array;ok=other!=null&&array.Rank==other.Rank&&Enumerable.Range(0,array.Rank).All(d=>array.GetLength(d)==other.GetLength(d))&&array.Cast<object>().SequenceEqual(other.Cast<object>());}if(!ok)throw new IOException("Typed result mismatch: "+value.Key);checks.Add(new{method=i,pass,label=value.Key,passed=true});}
 }bundle.VerifySources();Storage.WriteNew(Path.Combine(a[1],"typed-runtime-"+a[2]+".json"),new{passed=true,checks,source_files_unchanged=true,window_operations_used=false});
 }}return 0;}catch(Exception e){Storage.WriteNew(Path.Combine(a[1],"typed-runtime-failure-"+a[2]+".json"),new{phase,error=e.ToString(),checks});return 1;}finally{for(int i=refs.Count-1;i>=0;i--)LabView.Release(refs[i]);if(project!=null)try{LabView.Invoke(project,"Close",BindingFlags.InvokeMethod);}catch{}LabView.Release(context);LabView.Release(project);LabView.Release(app);}}
}
