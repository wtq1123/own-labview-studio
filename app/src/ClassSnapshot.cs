using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace OwnLabView {
// XML establishes copy locations only. Native library/member and callee
// checks establish the accepted class identity; XML never proves ancestry.
internal static class ClassSnapshot {
    internal sealed class Bundle {
        internal string Source, SourceRoot, Root, Target;
        internal Dictionary<string,string> Sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string,string> Copies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal List<object> Relinks=new List<object>();
        internal bool Contains(string path) {return path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        internal void VerifySources(){foreach(var p in Sources)if(!File.Exists(p.Key)||Data.Hash(p.Key)!=p.Value)throw new IOException("Class bundle source changed: "+p.Key);}
        internal bool VerifyDependency(string path) {
            if(!Contains(path))return false;
            string carrier=TypeImport.PhysicalCarrier(path);
            string hash;if(!Copies.TryGetValue(carrier,out hash)||Data.Hash(carrier)!=hash)throw new IOException("Class snapshot member/carrier was not pinned or changed: "+path);return true;
        }
        internal object[] Rows(){return Copies.Select(p=>new{path=p.Key,sha256=p.Value,source_path=Path.Combine(SourceRoot,p.Key.Substring(Root.Length+1)),source_sha256=Sources[Path.Combine(SourceRoot,p.Key.Substring(Root.Length+1))]}).Cast<object>().ToArray();}
    }
    static bool Within(string path,string root){return path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
    static XmlDocument Xml(string path){var doc=new XmlDocument{PreserveWhitespace=true,XmlResolver=null};using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=268435456}))doc.Load(reader);return doc;}
    internal static Bundle Plan(string source) {
        source=ViImport.CanonicalLocalPath(source);Storage.NoReparse(source);TypeImport.RejectVendorPath(source);
        if(!File.Exists(source)||!Path.GetExtension(source).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))throw new IOException("Actual external .lvclass file required.");
        if(NiDistribution.AcceptDependency(source))throw new ArgumentException("NI classes retain their original path instead of an external snapshot.");
        string root=Path.GetDirectoryName(source);var bundle=new Bundle{Source=source,SourceRoot=root};long bytes=0;
        foreach(string path in PhysicalFiles(root)) {
            Storage.NoReparse(path);if(!new[]{".vi",".ctl",".lvclass",".vim"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))continue;
            TypeImport.RejectVendorPath(path);
            bytes+=new FileInfo(path).Length;if(bytes>268435456||bundle.Sources.Count>=256)throw new InvalidDataException("Independent class bundle exceeds256 files/256 MiB: "+root);
            bundle.Sources.Add(Path.GetFullPath(path),Data.Hash(path));
        }
        if(!bundle.Sources.ContainsKey(source))throw new IOException("Selected class is absent from its bundle.");
        foreach(string path in bundle.Sources.Keys.Where(p=>Path.GetExtension(p).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)))CheckXml(path,Xml(path),bundle,null);
        bundle.VerifySources();return bundle;
    }
    static IEnumerable<string> PhysicalFiles(string root) {
        var queue=new Queue<KeyValuePair<string,int>>();queue.Enqueue(new KeyValuePair<string,int>(root,0));int directories=0;
        while(queue.Count!=0) {var item=queue.Dequeue();Storage.NoReparse(item.Key);if(item.Value>16||++directories>256)throw new InvalidDataException("Independent class folder budget exceeded: "+root);foreach(string path in Directory.EnumerateFiles(item.Key))yield return path;foreach(string child in Directory.EnumerateDirectories(item.Key)){Storage.NoReparse(child);queue.Enqueue(new KeyValuePair<string,int>(child,item.Value+1));}}
    }
    static string Resolve(string url,string document) {
        if(String.IsNullOrWhiteSpace(url))throw new InvalidDataException("Empty class member URL: "+document);
        // NI symbolic roots are retained and later checked by actual native Path.
        if(url.StartsWith("/<vilib>/",StringComparison.OrdinalIgnoreCase)||url.StartsWith("/<userlib>/",StringComparison.OrdinalIgnoreCase))return null;
        string value=Uri.UnescapeDataString(url).Replace('/',Path.DirectorySeparatorChar);
        if(value.StartsWith("\\",StringComparison.Ordinal)&&value.Length>3&&Char.IsLetter(value[1])&&value[2]=='\\')value=value[1]+":"+value.Substring(2);
        Uri absolute;if(Uri.TryCreate(url,UriKind.Absolute,out absolute)&&absolute.IsFile)value=absolute.LocalPath;
        // LabVIEW library URLs use the library file as a virtual directory:
        // a physical sibling method is ../Method.vi, while private data can
        // be an embedded Class.lvclass/Data.ctl resource.
        if(Path.IsPathRooted(value))return Path.GetFullPath(value);
        return new Uri(new Uri(document+Path.DirectorySeparatorChar),url.Replace('\\','/')).LocalPath;
    }
    static void CheckXml(string document,XmlDocument xml,Bundle bundle,string copiedDocument) {
        if(xml.DocumentElement==null||xml.DocumentElement.Name!="LVClass")throw new InvalidDataException("Selected class XML does not contain LVClass: "+document);
        foreach(XmlElement item in xml.SelectNodes("//Item[@URL]")) {
            string url=item.GetAttribute("URL"),path=Resolve(url,document);if(path==null)continue;TypeImport.RejectVendorPath(path);
            if(item.GetAttribute("Type")=="Class Private Data"&&!File.Exists(path))continue; // Embedded data is validated from native ProjectItem.VI.
            if(!Within(path,bundle.SourceRoot)) {if(NiDistribution.AcceptDependency(path))continue;throw new UnauthorizedAccessException("Class member is outside the selected independent bundle/NI: "+path);}
            if(!bundle.Sources.ContainsKey(path))throw new FileNotFoundException("Class member was not included in the physical snapshot.",path);
            Uri absolute;bool absoluteFile=Uri.TryCreate(url,UriKind.Absolute,out absolute)&&absolute.IsFile;
            if(copiedDocument!=null&&(absoluteFile||Path.IsPathRooted(Uri.UnescapeDataString(url).Replace('/',Path.DirectorySeparatorChar)))) {
                string target=Path.Combine(bundle.Root,path.Substring(bundle.SourceRoot.Length+1));item.SetAttribute("URL",new Uri(copiedDocument+Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(target)).ToString());bundle.Relinks.Add(new{document=copiedDocument,original_url=url,source_path=path,snapshot_path=target,new_url=item.GetAttribute("URL")});
            }
        }
    }
    internal static Bundle Copy(string source,string outputFolder,string rootName="class-snapshot") {
        if(rootName!="class-snapshot"&&rootName!="p")throw new ArgumentException("Internal snapshot root required.");
        var bundle=Plan(source);bundle.Root=Path.Combine(outputFolder,rootName);Storage.NoReparse(bundle.Root);Directory.CreateDirectory(bundle.Root);
        bundle.Target=Path.Combine(bundle.Root,source.Substring(bundle.SourceRoot.Length+1));
        foreach(var entry in bundle.Sources) {string target=Path.Combine(bundle.Root,entry.Key.Substring(bundle.SourceRoot.Length+1));Storage.NoReparse(target);Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=new FileStream(entry.Key,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);if(Data.Hash(target)!=entry.Value)throw new IOException("Class byte snapshot mismatch: "+entry.Key);}
        foreach(string original in bundle.Sources.Keys.Where(p=>Path.GetExtension(p).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))) {string copy=Path.Combine(bundle.Root,original.Substring(bundle.SourceRoot.Length+1));var xml=Xml(copy);int count=bundle.Relinks.Count;CheckXml(original,xml,bundle,copy);if(bundle.Relinks.Count!=count)xml.Save(copy);}
        foreach(string original in bundle.Sources.Keys){string copy=Path.Combine(bundle.Root,original.Substring(bundle.SourceRoot.Length+1));bundle.Copies.Add(copy,Data.Hash(copy));}bundle.VerifySources();return bundle;
    }
    internal static string ReportFor(string path) {
        string current=path;while(current.Length>3&&!File.Exists(current)&&!Directory.Exists(current)){int cut=current.LastIndexOf(Path.DirectorySeparatorChar);if(cut<3)return null;current=current.Substring(0,cut);}string directory=File.Exists(current)?Path.GetDirectoryName(current):current,projects=Path.Combine(Storage.Root,"projects");
        for(int depth=0;depth<20&&Within(directory,projects);depth++,directory=Path.GetDirectoryName(directory)) {string report=Path.Combine(directory,"prepared-file-report.json");if(File.Exists(report))return report;}return null;
    }
    internal static Bundle Accepted(string path) {
        string reportPath=ReportFor(path);if(reportPath==null)throw new UnauthorizedAccessException("External class requires prepare_file and an accepted own snapshot: "+path);
        Storage.NoReparse(reportPath);if(new FileInfo(reportPath).Length>1048576)throw new IOException("Class snapshot report exceeds1 MiB.");var report=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(reportPath,Encoding.UTF8)));
        if(!Object.Equals(report["created"],true)||!Object.Equals(report["native_identity_and_static_closure_checked"],true)||Data.Text(report,"category")!="class"||!Object.Equals(report["prepared_copy_created"],true))throw new InvalidDataException("Not an accepted class snapshot report.");
        string target=ViImport.CanonicalLocalPath(Data.Text(report,"vi_path")),snapshot=Path.Combine(Path.GetDirectoryName(reportPath),"class-snapshot");
        var bundle=new Bundle{Target=target,Root=snapshot,Source=Data.Text(report,"source_path"),SourceRoot=Data.Text(report,"source_bundle_root")};
        foreach(object value in Data.Array(report["snapshot_files"])) {var row=Data.Map(value);string member=Path.GetFullPath(Data.Text(row,"path"));Storage.NoReparse(member);if(!Within(member,snapshot)||!File.Exists(member)||!bundle.Copies.TryAddCompat(member,Data.Text(row,"sha256")))throw new IOException("Invalid or duplicate class snapshot file: "+member);}
        if(bundle.Copies.Count<1||bundle.Copies.Count>256||!bundle.Copies.ContainsKey(target)||bundle.Copies[target]!=Data.Text(report,"vi_sha256"))throw new InvalidDataException("Class snapshot target/hash mismatch.");
        foreach(var row in bundle.Copies)if(Data.Hash(row.Key)!=row.Value)throw new IOException("Class snapshot bytes changed: "+row.Key);if(!bundle.VerifyDependency(path))throw new UnauthorizedAccessException("Path is outside the accepted class snapshot.");return bundle;
    }
    static bool TryAddCompat(this Dictionary<string,string> map,string key,string value){if(map.ContainsKey(key))return false;map.Add(key,value);return true;}
    internal static bool HasReport(string path){string report=ReportFor(path);return report!=null&&Within(path,Path.Combine(Path.GetDirectoryName(report),"class-snapshot"));}
}
}
