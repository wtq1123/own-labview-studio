using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OwnLabView {
// Directory policy for static native dependencies, not publisher authentication.
// NI vi.lib can contain installed third-party components. In particular, this
// machine's old vendor tools installed a VIRobotics subtree there.
public static class NiDistribution {
    static readonly string[] Excluded = {"VIRobotics", "VIGenerator", "AgentForLabVIEW", "LabVIEWBuilder", "LabVIEWBridge"};
    public static string LibraryRoot { get { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(LabView.RegisteredExecutable()), "vi.lib")); } }
    public static bool AcceptDependency(string value) { return AcceptDependency(value, LibraryRoot); }
    public static bool AcceptDependency(string value, string root) {
        if(String.IsNullOrWhiteSpace(value)||String.IsNullOrWhiteSpace(root))return false;
        string path=Path.GetFullPath(value),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;
        string normalized=Regex.Replace(path,"[ _-]","");
        return !Excluded.Any(name=>normalized.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0);
    }
}
}
