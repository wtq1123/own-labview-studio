using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OwnLabView {
// Read-only disk preflight for app-authored CTL/global artifacts. A matching
// historical report does not validate NI format, current loaded identity or
// native dependencies; the consuming worker must perform those checks.
public sealed class GeneratedTypeDependency {
    const int MaxReportBytes = 1048576;
    const long MaxArtifactBytes = 268435456;
    string reportJson;
    public string Path { get; private set; }
    public string Hash { get; private set; }
    public string ReportPath { get; private set; }
    public string ReportHash { get; private set; }
    public string Kind { get; private set; }
    public int VIType { get; private set; }
    public string Sha256 { get { return Hash; } }
    public string ReportSha256 { get { return ReportHash; } }
    public bool NativeCalleesChecked { get { return false; } }
    public bool ViFormatVerified { get { return false; } }
    public bool LabViewCalled { get { return false; } }
    public Dictionary<string, object> ReportData { get { return Data.Map(Data.Json.DeserializeObject(reportJson)); } }
    GeneratedTypeDependency() { }

    static string CanonicalPath(string value) {
        if (String.IsNullOrWhiteSpace(value) || value.Length < 4 || !Char.IsLetter(value[0]) || value[1] != ':' || (value[2] != '\\' && value[2] != '/'))
            throw new ArgumentException("Type dependencies require absolute local drive paths.");
        if (value.Substring(2).IndexOf(':') >= 0 || value.Any(Char.IsControl) || value.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("Type dependency paths cannot contain streams or invalid characters.");
        foreach (string part in value.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) {
            if (part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Type dependency paths cannot contain traversal or ambiguous Windows segments.");
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9'))
                throw new ArgumentException("Windows device names are not type dependency paths.");
        }
        string full = System.IO.Path.GetFullPath(value);
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root, "projects")).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        Storage.NoReparse(root);
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Type dependencies must be under this application's projects folder.");
        string extension = System.IO.Path.GetExtension(full);
        if (!String.Equals(extension, ".ctl", StringComparison.OrdinalIgnoreCase) && !String.Equals(extension, ".vi", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Type dependencies must be .ctl controls or .vi globals.");
        Storage.NoReparse(full);
        if (!File.Exists(full)) throw new FileNotFoundException("Type dependency does not exist.", full);
        return full;
    }

    static string HashBytes(byte[] bytes) {
        using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    // Capture both identities while denying concurrent write/delete sharing on
    // the artifact and report. Once this method returns, callers must recheck
    // after their own transaction; this snapshot is not a persistent file lock.
    static string Snapshot(string artifactPath, string reportPath, out string artifactHash, out string reportHash) {
        Storage.NoReparse(artifactPath); Storage.NoReparse(reportPath);
        byte[] bytes;
        using (var artifact = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var report = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (artifact.Length < 1 || artifact.Length > MaxArtifactBytes) throw new InvalidDataException("Type artifact must contain 1..268435456 bytes.");
            if (report.Length < 1 || report.Length > MaxReportBytes) throw new InvalidDataException("Type report must contain 1..1048576 bytes.");
            using (var algorithm = SHA256.Create()) artifactHash = BitConverter.ToString(algorithm.ComputeHash(artifact)).Replace("-", "").ToLowerInvariant();
            bytes = new byte[(int)report.Length];
            int offset = 0;
            while (offset < bytes.Length) {
                int count = report.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new IOException("Type report truncated during snapshot.");
                offset += count;
            }
            if (report.ReadByte() >= 0) throw new IOException("Type report grew during snapshot.");
            Storage.NoReparse(artifactPath); Storage.NoReparse(reportPath);
        }
        reportHash = HashBytes(bytes);
        return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
    }

    static int IntegerField(Dictionary<string, object> report, string name) {
        object value;
        if (!report.TryGetValue(name, out value) || !(value is int || value is long))
            throw new InvalidDataException("Type report requires an integer " + name + ".");
        long integer = Convert.ToInt64(value);
        if (integer < Int32.MinValue || integer > Int32.MaxValue) throw new InvalidDataException("Type report integer is out of range: " + name);
        return (int)integer;
    }

    public static GeneratedTypeDependency ValidateOne(string path) {
        string full = CanonicalPath(path);
        string reportPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full), "type-report.json");
        string hash, reportHash;
        string json = Snapshot(full, reportPath, out hash, out reportHash);
        var report = Data.Map(Data.Json.DeserializeObject(json));
        object value;
        if (!report.TryGetValue("created", out value) || !Object.Equals(value, true) ||
            !report.TryGetValue("static_checks_passed", out value) || !Object.Equals(value, true))
            throw new InvalidDataException("Type dependency requires created=true and static_checks_passed=true in type-report.json.");
        string recorded = CanonicalPath(Data.Text(report, "vi_path"));
        if (!String.Equals(recorded, full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Type report identifies a different artifact.");
        if (!String.Equals(Data.Text(report, "vi_sha256"), hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Type artifact no longer matches its report hash.");
        string kind = Data.Text(report, "artifact_kind");
        int viType = IntegerField(report, "vi_type");
        bool control = kind == "control" && viType == 2 && String.Equals(System.IO.Path.GetExtension(full), ".ctl", StringComparison.OrdinalIgnoreCase);
        bool global = kind == "global" && viType == 3 && String.Equals(System.IO.Path.GetExtension(full), ".vi", StringComparison.OrdinalIgnoreCase);
        if (!control && !global) throw new InvalidDataException("Type report kind, VIType and file extension must match control/2/.ctl or global/3/.vi.");
        if (!report.TryGetValue("saved_direct_callees", out value) || !(value is object[]) || ((object[])value).Length != 0)
            throw new InvalidDataException("Type dependency currently requires an explicitly empty saved_direct_callees array.");
        if (report.ContainsKey("control_vi_type") && report["control_vi_type"] != null) {
            int controlType = IntegerField(report, "control_vi_type");
            if (!control || controlType < 1 || controlType > 3) throw new InvalidDataException("Reported control_vi_type must be 1..3 for a control artifact only.");
        }
        // Neither CTL nor global VI is an executable-program contract. ExecState
        // is retained as historical data, never used to demand runnable state 1.
        var accepted = new GeneratedTypeDependency { Path = full, Hash = hash, ReportPath = reportPath, ReportHash = reportHash, Kind = kind, VIType = viType, reportJson = json };
        accepted.VerifyUnchanged();
        return accepted;
    }

    public static GeneratedTypeDependency[] ValidateMany(IEnumerable<string> paths) {
        if (paths == null) throw new ArgumentNullException("paths");
        var result = new List<GeneratedTypeDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) {
            if (result.Count >= 64) throw new ArgumentException("At most 64 type dependencies are allowed in one set.");
            string full = CanonicalPath(path);
            if (!seen.Add(full)) throw new ArgumentException("Duplicate type dependency path.");
            if (!names.Add(System.IO.Path.GetFileName(full))) throw new ArgumentException("Type dependencies have conflicting filenames; native name resolution would be ambiguous.");
            result.Add(ValidateOne(full));
        }
        if (result.Count == 0) throw new ArgumentException("At least one type dependency is required.");
        foreach (var dependency in result) dependency.VerifyUnchanged();
        return result.ToArray();
    }

    public void VerifyUnchanged() {
        string full = CanonicalPath(Path);
        if (!String.Equals(full, Path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Type dependency path changed after preflight.");
        string hash, reportHash;
        Snapshot(full, ReportPath, out hash, out reportHash);
        if (!String.Equals(hash, Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Type dependency bytes changed after preflight.");
        if (!String.Equals(reportHash, ReportHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Type report bytes changed after preflight.");
    }

    public object ToEvidence() {
        return new { path = Path, sha256 = Hash, generation_report = ReportPath, report_sha256 = ReportHash, report_kind = "type", artifact_kind = Kind, vi_type = VIType,
            static_available_in_report = true, recorded_direct_callees_empty = true, native_callees_checked = false, vi_format_verified = false, labview_called = false,
            declared_dependency_recursion_performed = false,
            scope = "Matching historical type-report and unchanged disk bytes only. CTL/global artifacts are not required to be executable VIs. The NI worker must verify actual loaded Path, VIType, direct callees and placement compatibility; disk hashes do not authenticate VI format or unsaved NI memory." };
    }
}
}
