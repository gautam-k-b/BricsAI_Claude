using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsAI.Core
{
    public static class KnowledgeService
    {
        // ── Cache ────────────────────────────────────────────────────────────────
        // Parsed once per file-write; invalidated when LastWriteTime changes.
        private static DateTime _cacheWriteTime = DateTime.MinValue;
        private static List<string> _cachedLines = new();
        private static readonly object _cacheLock = new();

        private static readonly Regex _mappingRegex =
            new(@"Map the layer '(.*?)' to standard layer '(.*?)'\.", RegexOptions.Compiled);

        // ── Path ─────────────────────────────────────────────────────────────────
        private static string GetKnowledgePath()
        {
            // Allows test/mock runs to point at a scratch file instead of the real
            // learned-mappings data next to the executable.
            var overrideDir = Environment.GetEnvironmentVariable("BRICSAI_KNOWLEDGE_DIR");
            var basePath = string.IsNullOrWhiteSpace(overrideDir)
                ? AppDomain.CurrentDomain.BaseDirectory
                : overrideDir;
            var path = Path.Combine(basePath, "agent_knowledge.txt");

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(basePath);
                File.WriteAllText(path, "--- BricsAI Learned Rules & Preferences ---\n\n");
            }
            return path;
        }

        // ── Cache helpers ────────────────────────────────────────────────────────
        /// <summary>
        /// Returns raw lines (excluding header), re-reading from disk only when the
        /// file has been modified since the last parse.
        /// </summary>
        private static List<string> GetCachedLines()
        {
            var path = GetKnowledgePath();
            var writeTime = File.GetLastWriteTimeUtc(path);

            lock (_cacheLock)
            {
                if (writeTime != _cacheWriteTime)
                {
                    _cachedLines = File.ReadAllLines(path)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("---"))
                        .ToList();
                    _cacheWriteTime = writeTime;
                }
                return _cachedLines;
            }
        }

        private static void InvalidateCache()
        {
            lock (_cacheLock) { _cacheWriteTime = DateTime.MinValue; }
        }

        // ── Write ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Saves a rule or mapping. If the rule is a layer mapping whose source layer
        /// was already recorded, the old entry is updated in-place rather than
        /// appended, preventing unbounded file growth from duplicates.
        /// </summary>
        public static void SaveLearning(string rule)
        {
            try
            {
                var path = GetKnowledgePath();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string newEntry = $"[{timestamp}] {rule}";

                // Check whether this is a mapping rule so we can deduplicate.
                var match = _mappingRegex.Match(rule);
                if (match.Success)
                {
                    var srcLayer = match.Groups[1].Value.Trim();
                    var lines = File.ReadAllLines(path).ToList();
                    bool replaced = false;

                    for (int i = 0; i < lines.Count; i++)
                    {
                        var m = _mappingRegex.Match(lines[i]);
                        if (m.Success &&
                            string.Equals(m.Groups[1].Value.Trim(), srcLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = newEntry;
                            replaced = true;
                            break;
                        }
                    }

                    if (replaced)
                    {
                        File.WriteAllLines(path, lines);
                        InvalidateCache();
                        return;
                    }
                }

                // Not a duplicate mapping (or a free-form rule) — append.
                File.AppendAllText(path, newEntry + "\n");
                InvalidateCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving knowledge: {ex.Message}");
            }
        }

        // ── Read ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Returns the deduplicated knowledge base: one entry per mapping source
        /// (last write wins) plus all free-form rules, oldest-first. Uses the
        /// in-memory cache so repeated calls within a session are free.
        /// </summary>
        public static string GetLearnings()
        {
            try
            {
                var lines = GetCachedLines();
                if (!lines.Any()) return "No user rules learned yet.";

                // Deduplicate mapping lines — keep only the last entry per source layer.
                // Free-form rules (non-mapping lines) are always kept.
                var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduplicated = new List<string>();

                // Walk in reverse so the last (newest) entry for each source wins.
                foreach (var line in Enumerable.Reverse(lines))
                {
                    var m = _mappingRegex.Match(line);
                    if (m.Success)
                    {
                        var src = m.Groups[1].Value.Trim();
                        if (seenSources.Add(src))
                            deduplicated.Add(line); // first time we see this source (= newest)
                        // else skip — a newer entry for this source is already in the list
                    }
                    else
                    {
                        deduplicated.Add(line); // free-form rule — always keep
                    }
                }

                deduplicated.Reverse(); // restore chronological order
                return string.Join("\n", deduplicated);
            }
            catch
            {
                return "Error retrieving knowledge.";
            }
        }

        /// <summary>
        /// Returns the layer → target dictionary. Uses the same cache as GetLearnings().
        /// </summary>
        public static Dictionary<string, string> GetLayerMappingsDictionary()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in GetCachedLines())
                {
                    var m = _mappingRegex.Match(line);
                    if (m.Success && m.Groups.Count == 3)
                        dict[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim(); // last entry wins
                }
            }
            catch { }
            return dict;
        }

        // ── Maintenance ──────────────────────────────────────────────────────────
        /// <summary>
        /// Rewrites the file keeping only the newest entry per mapping source and all
        /// free-form rules. Call once on startup or on demand to remove duplicates
        /// accumulated from older versions of the app.
        /// </summary>
        public static void CompactFile()
        {
            try
            {
                var path = GetKnowledgePath();
                var allLines = File.ReadAllLines(path).ToList();

                var headerLines = allLines.Where(l => l.StartsWith("---")).ToList();
                var dataLines   = allLines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("---")).ToList();

                // Walk in reverse; keep newest per mapping source + all free-form rules.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var kept = new List<string>();
                foreach (var line in Enumerable.Reverse(dataLines))
                {
                    var m = _mappingRegex.Match(line);
                    if (m.Success)
                    {
                        if (seen.Add(m.Groups[1].Value.Trim()))
                            kept.Add(line);
                    }
                    else
                    {
                        kept.Add(line);
                    }
                }
                kept.Reverse();

                var output = headerLines.Concat(new[] { "" }).Concat(kept).Append("");
                File.WriteAllLines(path, output);
                InvalidateCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compacting knowledge file: {ex.Message}");
            }
        }
    }
}
