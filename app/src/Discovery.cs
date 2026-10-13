using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OwnLabView {
// Read-only filesystem discovery. A style match is metadata, not proof that a node can be created.
public static class Discovery {
    const int DefaultLimit = 30;
    const int EntryBudget = 10000, DirectoryBudget = 1000, DepthBudget = 12, TimeBudgetMs = 2000;
    sealed class Request { public string Query, RelativeFolder, Snapshot; public int Limit, Offset; }
    sealed class Match { public string Name, Path, RelativePath, Kind, Extension; public int Rank, Style; }
    sealed class PendingDirectory { public string Path; public int Depth; }

    public static object SearchStyles(Dictionary<string, object> args) {
        return SearchStylesFromFile(args, Path.Combine(Storage.Root, "recipes", "ni-styles.json"));
    }

    public static object FindExamples(Dictionary<string, object> args) {
        Parse(args, true); // Reject invalid requests even when NI is not installed.
        string root = DiscoverExamplesRoot();
        return FindExamplesInRoot(args, root, EntryBudget, DirectoryBudget, DepthBudget, TimeBudgetMs);
    }

    static Request Parse(Dictionary<string, object> args, bool examples) {
        if (args == null) throw new ArgumentException("Expected a JSON object.");
        Data.Keys(args, examples ? new[] { "query", "relative_folder", "limit", "offset", "snapshot_id" } : new[] { "query", "limit" });
        object value;
        string suppliedQuery;
        if (examples) {
            if (args.TryGetValue("query", out value) && !(value is string)) throw new ArgumentException("query must be a string when supplied.");
            suppliedQuery = value as string ?? "";
        } else suppliedQuery = Data.Text(args, "query");
        string query = suppliedQuery.Trim();
        if (query.Length > 200 || suppliedQuery.Any(Char.IsControl)) throw new ArgumentException("query must contain at most 200 characters without control characters.");
        int limit = DefaultLimit;
        if (args.TryGetValue("limit", out value)) {
            if (!(value is int || value is long) || Convert.ToInt64(value) < 1 || Convert.ToInt64(value) > 100)
                throw new ArgumentException("limit must be an integer from 1 to 100.");
            limit = Convert.ToInt32(value);
        }
        string relative = "";
        if (args.TryGetValue("relative_folder", out value)) {
            if (!(value is string)) throw new ArgumentException("relative_folder must be a string.");
            relative = (string)value;
            if (relative.Length > 240 || relative.Any(Char.IsControl) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || relative.IndexOfAny(new[] { '*', '?' }) >= 0)
                throw new ArgumentException("relative_folder must be a relative folder within the NI examples root.");
            foreach (string part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) {
                if (part == ".." || (part != "." && (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal))))
                    throw new ArgumentException("relative_folder cannot contain parent traversal or ambiguous Windows path segments.");
                if (part.EndsWith(".llb", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("LLB containers are opaque; their internal entries cannot be searched as folders.");
            }
        }
        int offset = 0;
        string snapshot = null;
        if (args.TryGetValue("offset", out value)) {
            if (!(value is int || value is long) || Convert.ToInt64(value) < 0 || Convert.ToInt64(value) > EntryBudget)
                throw new ArgumentException("offset must be an integer from 0 to " + EntryBudget + ".");
            offset = Convert.ToInt32(value);
        }
        if (args.TryGetValue("snapshot_id", out value)) {
            if (!(value is string) || ((string)value).Length != 64 || ((string)value).Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                throw new ArgumentException("snapshot_id must be the lowercase SHA-256 token from a directory browse response.");
            snapshot = (string)value;
        }
        if (query.Length != 0 && (args.ContainsKey("offset") || args.ContainsKey("snapshot_id")))
            throw new ArgumentException("offset and snapshot_id are only supported when query is omitted or empty (direct directory browsing).");
        return new Request { Query = query, RelativeFolder = relative, Limit = limit, Offset = offset, Snapshot = snapshot };
    }

    static int Rank(string name, string query) {
        if (String.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : -1;
    }
    static string MatchKind(int rank) { return rank == 0 ? "exact" : rank == 1 ? "prefix" : "contains"; }
    static IEnumerable<Match> Ordered(IEnumerable<Match> matches) {
        return matches.OrderBy(m => m.Rank).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.RelativePath, StringComparer.Ordinal);
    }

    // Private path injection keeps fixtures independent of the installed catalog and NI files.
    static object SearchStylesFromFile(Dictionary<string, object> args, string catalogPath) {
        Request request = Parse(args, false);
        catalogPath = Path.GetFullPath(catalogPath);
        Storage.NoReparse(catalogPath);
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("NI style metadata catalog is unavailable.", catalogPath);
        if (new FileInfo(catalogPath).Length > 1048576) throw new InvalidDataException("NI style metadata catalog exceeds the 1 MiB discovery limit.");
        var catalog = Data.Map(Data.Json.DeserializeObject(File.ReadAllText(catalogPath, Encoding.UTF8)));
        var matches = new List<Match>();
        foreach (var entry in catalog) {
            int rank = Rank(entry.Key, request.Query);
            if (rank < 0) continue;
            if (!(entry.Value is int || entry.Value is long) || Convert.ToInt64(entry.Value) < 0 || Convert.ToInt64(entry.Value) > Int32.MaxValue)
                throw new InvalidDataException("Invalid integer style metadata: " + entry.Key);
            matches.Add(new Match { Name = entry.Key, Style = Convert.ToInt32(entry.Value), Rank = rank });
        }
        return new {
            query = request.Query, limit = request.Limit, catalog_path = catalogPath,
            matches = Ordered(matches).Take(request.Limit).Select(m => new { name = m.Name, style = m.Style, match = MatchKind(m.Rank) }).ToArray(),
            total_matches = matches.Count, results_truncated = matches.Count > request.Limit,
            metadata_only = true, individually_tested = false, labview_called = false,
            notice = "Metadata candidates only. Individual styles have not been tested for creation or compatibility."
        };
    }

    static string DiscoverExamplesRoot() {
        // This workstation's confirmed target is LabVIEW 2024 x86. No COM activation or registry writes.
        var programRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        foreach (string programRoot in programRoots.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            string root = Path.Combine(programRoot, "National Instruments", "LabVIEW 2024", "examples");
            if (Directory.Exists(root)) { Storage.NoReparse(root); return Path.GetFullPath(root); }
        }
        throw new DirectoryNotFoundException("LabVIEW 2024 examples were not found in the standard NI installation folders. Custom installation paths are not configured.");
    }

    static string CanonicalDirectory(string path) {
        string full = Path.GetFullPath(path);
        string volume = Path.GetPathRoot(full);
        return full.Length == volume.Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    static bool Within(string root, string path) {
        return String.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    static string Relative(string root, string path) {
        return path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
    }

    static object FindExamplesInRoot(Dictionary<string, object> args, string examplesRoot, int maxEntries, int maxDirectories, int maxDepth, int maxMilliseconds) {
        Request request = Parse(args, true);
        if (maxEntries < 1 || maxDirectories < 1 || maxDepth < 0 || maxMilliseconds < 1) throw new ArgumentException("Invalid internal discovery budget.");
        string root = CanonicalDirectory(examplesRoot);
        Storage.NoReparse(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("NI examples root is unavailable: " + root);
        string searchRoot = CanonicalDirectory(Path.Combine(root, request.RelativeFolder));
        if (!Within(root, searchRoot)) throw new UnauthorizedAccessException("Search path escapes the NI examples root.");
        Storage.NoReparse(searchRoot);
        if (!Directory.Exists(searchRoot)) throw new DirectoryNotFoundException("Requested examples folder does not exist: " + searchRoot);
        if (String.Equals(Path.GetExtension(searchRoot), ".llb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("LLB containers are opaque; their internal entries cannot be searched as folders.");

        if (request.Query.Length == 0) return BrowseExamplesDirectory(request, root, searchRoot, maxEntries, maxMilliseconds);

        var timer = Stopwatch.StartNew();
        var pending = new Queue<PendingDirectory>();
        pending.Enqueue(new PendingDirectory { Path = searchRoot, Depth = 0 });
        var matches = new List<Match>();
        int examined = 0, visited = 0, skippedReparse = 0, skippedInaccessible = 0, skippedDepth = 0;
        string exhausted = null;
        while (pending.Count > 0 && exhausted == null) {
            if (timer.ElapsedMilliseconds >= maxMilliseconds) { exhausted = "time"; break; }
            if (visited >= maxDirectories) { exhausted = "directories"; break; }
            PendingDirectory next = pending.Dequeue();
            try {
                Storage.NoReparse(next.Path); // Recheck immediately before every traversal.
                visited++;
                using (var enumerator = Directory.EnumerateFileSystemEntries(next.Path).GetEnumerator()) {
                    while (true) {
                        if (timer.ElapsedMilliseconds >= maxMilliseconds) { exhausted = "time"; break; }
                        if (examined >= maxEntries) { exhausted = "entries"; break; }
                        if (!enumerator.MoveNext()) break;
                        examined++;
                        string path = Path.GetFullPath(enumerator.Current);
                        if (!Within(root, path)) throw new UnauthorizedAccessException("An enumerated entry escaped the examples root.");
                        FileAttributes attributes;
                        try { attributes = File.GetAttributes(path); }
                        catch (IOException) { skippedInaccessible++; continue; }
                        catch (UnauthorizedAccessException) { skippedInaccessible++; continue; }
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { skippedReparse++; continue; }
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        string extension = Path.GetExtension(path).ToLowerInvariant();
                        bool llb = extension == ".llb";
                        string name = Path.GetFileName(path);
                        int rank = Rank(name, request.Query);
                        if (rank >= 0) matches.Add(new Match { Name = name, Path = path, RelativePath = Relative(root, path), Rank = rank, Extension = extension, Kind = llb ? "llb_container" : directory ? "directory" : extension == ".vi" ? "vi" : "file" });
                        if (directory && !llb) {
                            if (next.Depth >= maxDepth) skippedDepth++;
                            else pending.Enqueue(new PendingDirectory { Path = path, Depth = next.Depth + 1 });
                        }
                    }
                }
            } catch (UnauthorizedAccessException) {
                // Includes a directory replaced by a reparse point after it was queued.
                skippedInaccessible++;
            } catch (IOException) { skippedInaccessible++; }
        }
        timer.Stop();
        return new {
            mode = "search", recursive = true,
            query = request.Query, limit = request.Limit, examples_root = root, search_root = searchRoot,
            relative_folder = String.Equals(root, searchRoot, StringComparison.OrdinalIgnoreCase) ? "" : Relative(root, searchRoot),
            matches = Ordered(matches).Take(request.Limit).Select(m => new { name = m.Name, path = m.Path, relative_path = m.RelativePath, kind = m.Kind, extension = m.Extension, match = MatchKind(m.Rank) }).ToArray(),
            total_matches_in_examined_entries = matches.Count, results_truncated = matches.Count > request.Limit,
            search_complete = exhausted == null && skippedReparse == 0 && skippedInaccessible == 0 && skippedDepth == 0,
            budget_exhausted = exhausted != null || skippedDepth > 0, exhausted_budget = exhausted ?? (skippedDepth > 0 ? "depth" : null),
            examined_entries = examined, visited_directories = visited, skipped_reparse_count = skippedReparse,
            skipped_inaccessible_count = skippedInaccessible, skipped_depth_count = skippedDepth,
            entry_budget = maxEntries, directory_budget = maxDirectories, max_depth = maxDepth, time_budget_ms = maxMilliseconds,
            elapsed_ms = timer.ElapsedMilliseconds, time_budget_is_cooperative = true,
            llb_contents_enumerated = false, file_contents_read = false, labview_called = false,
            notice = "Read-only filesystem names. LLB files are returned only as containers; internal VIs are not enumerated. A partial search ranks only examined entries. The time budget is checked between filesystem calls."
        };
    }

    static object BrowseExamplesDirectory(Request request, string root, string folder, int maxEntries, int maxMilliseconds) {
        var timer = Stopwatch.StartNew();
        var entries = new List<Match>();
        int examined = 0, skippedReparse = 0, skippedInaccessible = 0;
        string exhausted = null;
        bool traversalFinished = false;
        try {
            Storage.NoReparse(folder);
            using (var enumerator = Directory.EnumerateFileSystemEntries(folder).GetEnumerator()) {
                while (true) {
                    if (timer.ElapsedMilliseconds >= maxMilliseconds) { exhausted = "time"; break; }
                    if (!enumerator.MoveNext()) { traversalFinished = true; break; }
                    if (examined >= maxEntries) { exhausted = "entries"; break; }
                    examined++;
                    string path = Path.GetFullPath(enumerator.Current);
                    if (!Within(root, path)) throw new UnauthorizedAccessException("An enumerated entry escaped the examples root.");
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (IOException) { skippedInaccessible++; continue; }
                    catch (UnauthorizedAccessException) { skippedInaccessible++; continue; }
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skippedReparse++; continue; }
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    bool llb = extension == ".llb";
                    if (!directory && extension != ".vi" && !llb) continue;
                    entries.Add(new Match { Name = Path.GetFileName(path), Path = path, RelativePath = Relative(root, path), Extension = extension, Kind = llb ? "llb_container" : directory ? "directory" : "vi" });
                }
            }
        } catch (UnauthorizedAccessException) { skippedInaccessible++; }
        catch (IOException) { skippedInaccessible++; }
        timer.Stop();
        var ordered = Ordered(entries).ToArray();
        string snapshot;
        // Names/kinds determine pagination. No file bytes or VI contents are read.
        using (var hash = SHA256.Create()) {
            Action<string> digest = value => {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                byte[] length = BitConverter.GetBytes(bytes.Length);
                hash.TransformBlock(length, 0, length.Length, length, 0);
                hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            };
            digest(folder);
            foreach (var entry in ordered) { digest(entry.Name); digest(entry.Kind); }
            hash.TransformFinalBlock(new byte[0], 0, 0);
            snapshot = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }
        if (request.Snapshot != null && !String.Equals(request.Snapshot, snapshot, StringComparison.Ordinal))
            throw new InvalidOperationException("Examples directory listing changed since the previous page; restart browsing with offset 0 and no snapshot_id.");
        if (request.Offset > ordered.Length) throw new ArgumentException("offset is beyond the current directory listing; restart from offset 0.");
        var page = ordered.Skip(request.Offset).Take(request.Limit).ToArray();
        int end = request.Offset + page.Length;
        int? next = traversalFinished && end < ordered.Length ? (int?)end : null;
        bool complete = traversalFinished && skippedReparse == 0 && skippedInaccessible == 0;
        string relative = String.Equals(root, folder, StringComparison.OrdinalIgnoreCase) ? "" : Relative(root, folder);
        return new {
            mode = "browse", recursive = false, query = "", limit = request.Limit, offset = request.Offset,
            examples_root = root, search_root = folder, base_path = folder, relative_folder = relative,
            matches = page.Select(m => new { name = m.Name, path = m.Path, relative_path = m.RelativePath, kind = m.Kind, extension = m.Extension }).ToArray(),
            vi_names = page.Where(m => m.Kind == "vi").Select(m => m.Name).ToArray(),
            folder_names = page.Where(m => m.Kind == "directory").Select(m => m.Name).ToArray(),
            llb_container_names = page.Where(m => m.Kind == "llb_container").Select(m => m.Name).ToArray(),
            total_matches_in_examined_entries = ordered.Length, results_truncated = end < ordered.Length || !complete,
            search_complete = complete, listing_complete = complete, all_pages_returned = request.Offset == 0 && end == ordered.Length && complete,
            next_offset = next, has_more_pages = next.HasValue, snapshot_id = snapshot,
            next_page_args = next.HasValue ? new Dictionary<string, object> { { "relative_folder", relative }, { "limit", request.Limit }, { "offset", next.Value }, { "snapshot_id", snapshot } } : null,
            pagination_available = traversalFinished, snapshot_scope = "Directory entry names and kinds only; use next_page_args to reject changes between pages. File bytes and VI identity are not examined.",
            budget_exhausted = exhausted != null, exhausted_budget = exhausted,
            examined_entries = examined, visited_directories = 1, skipped_reparse_count = skippedReparse, skipped_inaccessible_count = skippedInaccessible,
            entry_budget = maxEntries, time_budget_ms = maxMilliseconds, elapsed_ms = timer.ElapsedMilliseconds, time_budget_is_cooperative = true,
            llb_contents_enumerated = false, file_contents_read = false, labview_called = false,
            notice = "Direct children only: VI names and subfolder names are page-local; combine all pages for a complete listing when listing_complete is true. LLB names are separate opaque containers. Incomplete scans do not issue a continuation; skipped reparse/inaccessible entries remain disclosed."
        };
    }
}
}
