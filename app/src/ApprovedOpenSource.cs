using System;
using System.IO;
using System.Text;
namespace OwnLabView {
// Only exact, locally pinned MIT files may be additional helper dependencies.
// This is never a blanket authorization for arbitrary external VIs.
public static class ApprovedOpenSource {
 public static bool Accept(string path){
  string manifest=Path.Combine(Storage.Root,"approved-open-source.json");if(!File.Exists(manifest))return false;
  Storage.NoReparse(manifest);if(new FileInfo(manifest).Length>1048576)throw new IOException("Open source manifest too large.");
  var entries=Data.Map(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(manifest,Encoding.UTF8)))["files"]);
  foreach(var pair in entries){string file=Path.GetFullPath(Path.Combine(Storage.Root,pair.Key));if(!file.StartsWith(Storage.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Open source dependency escaped application.");
   if(!String.Equals(file,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))continue;Storage.NoReparse(file);if(Data.Hash(file)!=Convert.ToString(pair.Value))throw new IOException("Approved MIT dependency changed: "+file);return true;}
  return false;
 }
}
}
