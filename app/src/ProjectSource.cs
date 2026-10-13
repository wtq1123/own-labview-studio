using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace OwnLabView {
// A member-edit transaction starts from a saved application-generated project.
// Work on a new XML copy before opening it in NI, so the original NI project is
// never opened or saved by this operation. More complex project types remain
// outside this deliberately stated direct-member editing contract.
public sealed class ProjectSource {
    public string Path, Hash, ReportPath, ReportHash;
    public string[] MemberPaths;
    static string Root {get{return System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root,"projects"))+System.IO.Path.DirectorySeparatorChar;}}
    static string LocalPath(string value,string extension) {
        if(String.IsNullOrWhiteSpace(value)||!System.IO.Path.IsPathRooted(value))throw new ArgumentException("Project source requires an absolute local path.");
        string path=System.IO.Path.GetFullPath(value);
        if(!path.StartsWith(Root,StringComparison.OrdinalIgnoreCase)||!(extension==".member"?new[]{".vi",".ctl",".lvclass",".lvlib"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase):System.IO.Path.GetExtension(path).Equals(extension,StringComparison.OrdinalIgnoreCase))||path.Substring(2).Contains(":"))throw new UnauthorizedAccessException("Project source must be an application-generated "+extension+" inside projects.");
        Storage.NoReparse(path);
        if(!File.Exists(path))throw new FileNotFoundException("Project source is missing",path);
        return path;
    }
    static XmlDocument ReadXml(string path) {
        var xml=new XmlDocument{XmlResolver=null};
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024};
        using(var reader=XmlReader.Create(path,settings))xml.Load(reader);
        if(xml.DocumentElement==null||xml.DocumentElement.Name!="Project")throw new InvalidDataException("Expected a LabVIEW project XML root.");
        var targets=xml.SelectNodes("/Project/Item");
        if(targets.Count!=1||targets[0].Attributes["Type"]==null||targets[0].Attributes["Type"].Value!="My Computer")throw new ArgumentException("Project copying currently supports one My Computer target.");
        foreach(XmlNode child in targets[0].SelectNodes("Item")) {
            string type=child.Attributes["Type"]==null?null:child.Attributes["Type"].Value;
            if(type!="VI"&&type!="LVClass"&&type!="Library"&&type!="Folder"&&type!="Dependencies"&&type!="Build")throw new ArgumentException("Unsupported project member category for this editing transaction: "+type);
            if(type!="Dependencies"&&type!="Build"&&type!="Folder"&&child.SelectNodes("Item").Count>0)throw new ArgumentException("Nested project items or build specifications require a separate editing contract.");
        }
        return xml;
    }
    static string Resolve(string projectPath,string url) {
        if(String.IsNullOrWhiteSpace(url))throw new InvalidDataException("VI member URL is missing.");
        string decoded=Uri.UnescapeDataString(url);Uri absolute;
        if(Uri.TryCreate(decoded,UriKind.Absolute,out absolute)&&absolute.IsFile)return LocalPath(absolute.LocalPath,".member");
        if(decoded.Contains("://"))throw new ArgumentException("Project members must be local files.");
        return LocalPath(System.IO.Path.Combine(projectPath,decoded.Replace('/',System.IO.Path.DirectorySeparatorChar)),".member");
    }
    public static ProjectSource Validate(string value) {
        string path=LocalPath(value,".lvproj");
        string report=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"project-report.json");
        Storage.NoReparse(report);
        if(!File.Exists(report)||new FileInfo(report).Length>1048576)throw new ArgumentException("Project source requires a bounded successful project report.");
        string hash=Data.Hash(path),reportHash=Data.Hash(report);
        var evidence=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(report,Encoding.UTF8)));
        object created;
        if(!evidence.TryGetValue("created",out created)||!Object.Equals(created,true)||!String.Equals(Data.Text(evidence,"project_path"),path,StringComparison.OrdinalIgnoreCase)||!String.Equals(Data.Text(evidence,"project_sha256"),hash,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Project source no longer matches its successful report.");
        XmlDocument xml=ReadXml(path);
        var members=new List<string>();
        foreach(XmlNode child in xml.SelectNodes("/Project/Item[@Type='My Computer']//Item[(@Type='VI' or @Type='LVClass' or @Type='Library') and not(ancestor::Item[@Type='Dependencies'])]")) {
            string p=Resolve(path,child.Attributes["URL"]==null?null:child.Attributes["URL"].Value);
            Projects.ValidateMember(p);
            if(members.Contains(p,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Duplicate source project member.");
            members.Add(p);
        }
        if(members.Count>64)throw new ArgumentException("Project source requires 0..64 audited members.");
        var result=new ProjectSource{Path=path,Hash=hash,ReportPath=report,ReportHash=reportHash,MemberPaths=members.ToArray()};
        result.VerifyUnchanged();return result;
    }
    public void VerifyUnchanged() {
        Storage.NoReparse(Path);Storage.NoReparse(ReportPath);
        if(Data.Hash(Path)!=Hash||Data.Hash(ReportPath)!=ReportHash)throw new IOException("Source project or its report changed during this transaction.");
    }
    public void WriteCopy(string destination,IEnumerable<string> members) {
        VerifyUnchanged();Storage.NoReparse(destination);
        if(System.IO.Path.GetFullPath(destination).Equals(Path,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source project cannot be overwritten.");
        XmlDocument xml=ReadXml(Path);
        XmlNode computer=xml.SelectSingleNode("/Project/Item[@Type='My Computer']");
        // Dependency items are NI-derived. Recompute them from the requested
        // member set instead of retaining old relative links in the new copy.
        foreach(XmlNode old in computer.SelectNodes("Item[@Type='Build']/Item"))old.ParentNode.RemoveChild(old);
        foreach(XmlNode old in computer.SelectNodes("Item[@Type='Dependencies']/Item"))old.ParentNode.RemoveChild(old);
        foreach(XmlNode old in computer.SelectNodes(".//Item[(@Type='VI' or @Type='LVClass' or @Type='Library') and not(ancestor::Item[@Type='Dependencies'])]"))old.ParentNode.RemoveChild(old);
        foreach(string member in members) {
            var item=xml.CreateElement("Item");
            item.SetAttribute("Name",System.IO.Path.GetFileName(member));item.SetAttribute("Type",System.IO.Path.GetExtension(member).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)?"LVClass":System.IO.Path.GetExtension(member).Equals(".lvlib",StringComparison.OrdinalIgnoreCase)?"Library":"VI");
            // NI's relative URL base includes the project filename as a path
            // component; OpenProject acceptance established this convention.
            item.SetAttribute("URL",new Uri(destination+System.IO.Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(member)).ToString());
            XmlNode special=computer.SelectSingleNode("Item[@Type='Dependencies' or @Type='Build']");
            if(special==null)computer.AppendChild(item);else computer.InsertBefore(item,special);
        }
        using(var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true}))xml.Save(writer);
        VerifyUnchanged();
    }
    public object Evidence() {VerifyUnchanged();return new{path=Path,before_sha256=Hash,after_sha256=Data.Hash(Path),report_path=ReportPath,report_sha256=ReportHash,unchanged=true,opened_in_labview=false,saved_in_labview=false};}
}
}
