using System;using System.IO;using System.Linq;
namespace OwnLabView {
internal static class CompletionStatus {
 internal static readonly string Report=Path.GetFullPath(Path.Combine(Storage.Root,"..","reports","completion-acceptance-20261013.json"));
 internal static bool Accepted(){try{if(!File.Exists(Report))return false;var doc=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(Report)));if(!Object.Equals(doc["agreed_scope_acceptance_passed"],true))return false;var files=Data.Array(doc["production_snapshot"]).Select(Data.Map).ToArray();string exe=Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe");if(!files.Any(r=>Data.Text(r,"path").Equals(exe,StringComparison.OrdinalIgnoreCase)))return false;return files.All(r=>File.Exists(Data.Text(r,"path"))&&Data.Hash(Data.Text(r,"path"))==Data.Text(r,"sha256"));}catch{return false;}}
 internal static bool LegacyPresent(){string parent=Path.GetFullPath(Path.Combine(Storage.Root,"..",".."));return Directory.Exists(Path.Combine(parent,"LabVIEWBuilder"))||Directory.Exists(Path.Combine(parent,"LabVIEWBridge"));}
}
}
