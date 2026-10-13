using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using OwnLabView;
// Filesystem/XML fixtures only. The text .vi files below are never sent to NI.
public static class ProjectSourceAcceptance {
    static readonly List<object> checks=new List<object>();
    static void Check(string name,bool ok){checks.Add(new{name,passed=ok});if(!ok)throw new Exception(name);}
    static void Reject(string name,Action action){try{action();}catch(ArgumentException){Check(name,true);return;}catch(IOException){Check(name,true);return;}catch(UnauthorizedAccessException){Check(name,true);return;}catch(XmlException){Check(name,true);return;}throw new Exception("Unexpected acceptance: "+name);}
    static string Put(string path,string content){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content,new UTF8Encoding(false));return path;}
    static string Vi(string name){string path=Put(Path.Combine(Storage.Root,"projects",name,name+".vi"),"OFFLINE TEXT FIXTURE; NOT A LABVIEW VI");Put(Path.Combine(Path.GetDirectoryName(path),"build-report.json"),Data.Json.Serialize(new{created=true,vi_path=path,vi_sha256=Data.Hash(path),execution_state=1}));return path;}
    static string Project(string name,string xml){string path=Put(Path.Combine(Storage.Root,"projects",name,name+".lvproj"),xml);Report(path);return path;}
    static void Report(string path){Put(Path.Combine(Path.GetDirectoryName(path),"project-report.json"),Data.Json.Serialize(new{created=true,project_path=path,project_sha256=Data.Hash(path)}));}
    public static int Main(){try{
        if(!Path.GetFileName(Storage.Root).StartsWith("project-source-acceptance-",StringComparison.Ordinal))throw new Exception("Run only in an isolated test fixture/bin.");
        string a=Vi("Alpha"),b=Vi("中文 Beta");
        string xml="<Project Type='Project'><Property Name='custom' Type='Str'>preserve-me</Property><Item Name='My Computer' Type='My Computer'><Property Name='server.tcp.enabled' Type='Bool'>false</Property><Item Name='Alpha.vi' Type='VI' URL='../../Alpha/Alpha.vi'/><Item Name='Dependencies' Type='Dependencies'/><Item Name='Build' Type='Build'/></Item></Project>";
        string source=Project("Source",xml);var accepted=ProjectSource.Validate(source);
        Check("NI file-based relative member URL resolves",accepted.MemberPaths.SequenceEqual(new[]{a}));
        string sourceHash=Data.Hash(source),sourceReportHash=Data.Hash(accepted.ReportPath);
        string copy=Path.Combine(Storage.Root,"projects","Edited","Edited.lvproj");Directory.CreateDirectory(Path.GetDirectoryName(copy));accepted.WriteCopy(copy,new[]{b});Report(copy);
        Check("copy replaces membership and supports Unicode paths",ProjectSource.Validate(copy).MemberPaths.SequenceEqual(new[]{b}));
        Check("source project and report stay unchanged",Data.Hash(source)==sourceHash&&Data.Hash(accepted.ReportPath)==sourceReportHash);
        var copied=new XmlDocument{XmlResolver=null};copied.Load(copy);
        Check("VI items precede NI Dependencies and Build sections",copied.SelectNodes("/Project/Item/Item[@Type='VI']/preceding-sibling::Item[@Type='Dependencies' or @Type='Build']").Count==0);
        Check("project and target properties survive membership edit",copied.SelectSingleNode("/Project/Property[@Name='custom']").InnerText=="preserve-me"&&copied.SelectSingleNode("/Project/Item/Property[@Name='server.tcp.enabled']").InnerText=="false");
        Reject("existing destination is never overwritten",()=>accepted.WriteCopy(copy,new[]{a}));
        Reject("source cannot be overwritten",()=>accepted.WriteCopy(source,new[]{b}));
        Reject("relative source path rejected",()=>ProjectSource.Validate("Source.lvproj"));
        Reject("outside project root rejected",()=>ProjectSource.Validate(Path.Combine(Storage.Root,"outside.lvproj")));
        string nested=Project("Nested",xml.Replace("<Item Name='Build' Type='Build'/>","<Item Name='Folder' Type='Folder'><Item Name='Alpha.vi' Type='VI' URL='../../Alpha/Alpha.vi'/></Item>"));
        Reject("duplicate path across root and folder rejected",()=>ProjectSource.Validate(nested));
        string folderXml=xml.Replace("<Item Name='Alpha.vi' Type='VI' URL='../../Alpha/Alpha.vi'/>","<Item Name='Logic' Type='Folder'><Item Name='Alpha.vi' Type='VI' URL='../../Alpha/Alpha.vi'/></Item>");
        var folderSource=ProjectSource.Validate(Project("FolderSource",folderXml));Check("nested virtual folder member accepted",folderSource.MemberPaths.SequenceEqual(new[]{a}));
        var layout=ProjectLayout.Parse(new Dictionary<string,object>(),new[]{a},folderSource);Check("native folder layout extracted",layout.Members[a]=="Logic"&&layout.Folders.Contains("Logic"));
        Reject("RT target rejected",()=>ProjectSource.Validate(Project("RtTarget",xml.Replace("Type='My Computer'","Type='RT Target'"))));
        Reject("unknown build kind rejected",()=>BuildSpecs.Parse(new Dictionary<string,object>{{"build_specs",new object[]{new Dictionary<string,object>{{"name","Bad"},{"kind","FPGA"},{"source_path",a}}}}},new[]{a},null));
        Reject("build path outside project members rejected",()=>BuildSpecs.Parse(new Dictionary<string,object>{{"build_specs",new object[]{new Dictionary<string,object>{{"name","Bad"},{"kind","EXE"},{"source_path",b}}}}},new[]{a},null));
        Reject("folder path traversal rejected",()=>ProjectLayout.Parse(new Dictionary<string,object>{{"member_folders",new Dictionary<string,object>{{a,"../bad"}}}},new[]{a},null));
        string dtd=Project("Dtd","<!DOCTYPE Project [<!ENTITY x 'x'>]>"+xml);
        Reject("DTD rejected",()=>ProjectSource.Validate(dtd));
        string stale=Project("Stale",xml);File.AppendAllText(stale," ");Reject("changed source report hash rejected",()=>ProjectSource.Validate(stale));
        string reportTamper=Project("ReportTamper",xml);var record=ProjectSource.Validate(reportTamper);File.AppendAllText(record.ReportPath," ");Reject("post-validation report changes rejected",()=>record.VerifyUnchanged());
        var preflight=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(Projects.Validate(new Dictionary<string,object>{{"name","NewCopy"},{"vi_paths",new object[]{b}},{"source_project",source}}))));
        Check("copy-edit preflight remains filesystem-only",(bool)preflight["valid"]&&!(bool)preflight["labview_called"]&&!(bool)preflight["vi_format_verified"]);
        string json=Data.Json.Serialize(new{passed=true,count=checks.Count,checks,labview_called=false,scope="Synthetic text VI and XML fixtures only; not NI format, compilation, runtime or reopen evidence."});
        Put(Path.Combine(Storage.Root,"acceptance.json"),json);Console.WriteLine(json);return 0;
    }catch(Exception ex){Console.Error.WriteLine(Data.Error(ex));return 1;}}
}
