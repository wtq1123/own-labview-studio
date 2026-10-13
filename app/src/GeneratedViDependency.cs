using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OwnLabView {
// Disk preflight for a future restricted subVI placement operation. This class
// never opens a VI through NI, follows native callees, or asserts VI authenticity.
public sealed class GeneratedViDependency {
    const int MaxReportBytes = 1048576;
    string reportJson;
    public string Path { get; private set; }
    public string Sha256 { get; private set; }
    public string ReportPath { get; private set; }
    public string ReportSha256 { get; private set; }
    public string ReportKind { get; private set; }
    public bool NativeCalleesChecked { get { return false; } }
    public bool ViFormatVerified { get { return false; } }
    public bool LabViewCalled { get { return false; } }
    // Return an independent parsed copy so callers cannot mutate the stored evidence.
    public Dictionary<string, object> ReportData { get { return Data.Map(Data.Json.DeserializeObject(reportJson)); } }
    GeneratedViDependency() { }

    static string ProjectsRoot() {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Root, "projects"));
        Storage.NoReparse(root);
        return root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    static string CanonicalViPath(string value) {
        if (String.IsNullOrWhiteSpace(value) || value.Length < 4 || !Char.IsLetter(value[0]) || value[1] != ':' || (value[2] != '\\' && value[2] != '/'))
            throw new ArgumentException("Generated VI dependencies require absolute local drive paths.");
        if (value.Substring(2).IndexOf(':') >= 0 || value.Any(Char.IsControl) || value.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("Alternate streams and invalid path characters are not accepted.");
        foreach (string part in value.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) {
            if (part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Generated VI dependency paths cannot contain traversal or ambiguous Windows segments.");
        }
        string full = System.IO.Path.GetFullPath(value);
        string root = ProjectsRoot();
        if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Generated VI dependencies must be under this application's projects folder.");
        if (!String.Equals(System.IO.Path.GetExtension(full), ".vi", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Generated VI dependencies must be .vi files.");
        Storage.NoReparse(full);
        if (!File.Exists(full)) throw new FileNotFoundException("Generated VI dependency does not exist.", full);
        return full;
    }

    static string ReadReport(string path, out string hash) {
        Storage.NoReparse(path);
        byte[] bytes;
        // Deny write/delete sharing while capturing the exact bounded report bytes.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (stream.Length < 1 || stream.Length > MaxReportBytes) throw new InvalidDataException("Generation report must contain 1..1048576 bytes: " + path);
            bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length) {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new IOException("Generation report was truncated while reading: " + path);
                offset += read;
            }
            if (stream.ReadByte() >= 0) throw new IOException("Generation report grew while reading: " + path);
        }
        using (var algorithm = SHA256.Create()) hash = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    static bool StateIsRunnable(Dictionary<string, object> report) {
        object state;
        return report.TryGetValue("execution_state", out state) && (state is int || state is long) && Convert.ToInt64(state) == 1;
    }

    public static GeneratedViDependency ValidateOne(string path) {
        string full = CanonicalViPath(path), hash = Data.Hash(full);
        GeneratedViDependency accepted = null;
        foreach (string filename in new[] { "build-report.json", "recipe-report.json" }) {
            string candidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full), filename);
            if (!File.Exists(candidate)) continue;
            string reportHash;
            string json = ReadReport(candidate, out reportHash);
            var report = Data.Map(Data.Json.DeserializeObject(json));
            object created;
            if (!report.TryGetValue("created", out created) || !Object.Equals(created, true)) continue;
            string recorded = CanonicalViPath(Data.Text(report, "vi_path"));
            if (!String.Equals(recorded, full, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Generation report identifies a different VI: " + candidate);
            if (!String.Equals(Data.Text(report, "vi_sha256"), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated VI dependency no longer matches its generation report: " + full);
            if (!StateIsRunnable(report)) throw new InvalidDataException("Generation report does not record executable state 1: " + candidate);
            object staticPassed;
            bool hasStatic = report.TryGetValue("static_checks_passed", out staticPassed);
            if ((filename == "recipe-report.json" && !hasStatic) || (hasStatic && !Object.Equals(staticPassed, true)))
                throw new InvalidDataException("Generation report does not record successful static checks: " + candidate);
            if (accepted != null) throw new InvalidDataException("Multiple successful generation reports identify the same VI; provenance is ambiguous: " + full);
            accepted = new GeneratedViDependency { Path = full, Sha256 = hash, ReportPath = candidate, ReportSha256 = reportHash, ReportKind = filename == "build-report.json" ? "build" : "recipe", reportJson = json };
        }
        if (accepted == null) throw new ArgumentException("Generated VI dependency requires a matching successful build-report.json or recipe-report.json: " + full);
        accepted.VerifyUnchanged();
        return accepted;
    }

    // Collision scope is this explicit dependency set, not every historical VI
    // in projects. Repeated placement can reuse the same validated object.
    public static GeneratedViDependency[] ValidateMany(IEnumerable<string> paths) {
        if (paths == null) throw new ArgumentNullException("paths");
        var accepted = new List<GeneratedViDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) {
            if (accepted.Count >= 64) throw new ArgumentException("At most 64 generated VI dependencies are allowed in one set.");
            string full = CanonicalViPath(path);
            if (!seen.Add(full)) throw new ArgumentException("Duplicate generated VI dependency path: " + full);
            if (!names.Add(System.IO.Path.GetFileName(full))) throw new ArgumentException("Generated VI dependencies have the same filename at different paths; LabVIEW name resolution would be ambiguous.");
            accepted.Add(ValidateOne(full));
        }
        if (accepted.Count == 0) throw new ArgumentException("At least one generated VI dependency is required.");
        foreach (var dependency in accepted) dependency.VerifyUnchanged();
        return accepted.ToArray();
    }

    public void VerifyUnchanged() {
        string full = CanonicalViPath(Path);
        if (!String.Equals(full, Path, StringComparison.OrdinalIgnoreCase) || !String.Equals(Data.Hash(full), Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Generated VI dependency changed after preflight: " + Path);
        Storage.NoReparse(ReportPath);
        if (!File.Exists(ReportPath)) throw new IOException("Generation report is missing after preflight: " + ReportPath);
        string reportHash;
        ReadReport(ReportPath, out reportHash);
        if (!String.Equals(reportHash, ReportSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Generation report changed after preflight: " + ReportPath);
    }

    public object ToEvidence() {
        return new { path = Path, sha256 = Sha256, generation_report = ReportPath, report_sha256 = ReportSha256, report_kind = ReportKind,
            static_available_in_report = true, native_callees_checked = false, vi_format_verified = false, labview_called = false,
            declared_dependency_recursion_performed = false,
            scope = "Matching generation report and unchanged disk bytes only. Report static state is historical evidence, not a current NI check or proof of VI authenticity. Native callee closure, loaded-path identity and type/connector compatibility must be verified by the NI worker. No helper_callees are interpreted as generated VI dependencies." };
    }
}
}
