using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BricsAI.Core
{
    /// <summary>
    /// Persists learned layer mappings and free-form behavioral rules in a local SQLite database
    /// (one embedded file, no server). Layer mappings are upserted by source layer (indexed
    /// primary key), so reads never need to scan/parse the whole knowledge base. A copy of this
    /// database is checked into the repo at solution level as the starter/seed copy — on a new
    /// machine, copy it to %LOCALAPPDATA%\BricsAI\agent_knowledge.db (or the BRICSAI_KNOWLEDGE_DIR
    /// override path) before first run; this class only ever reads/writes that local copy, never
    /// the checked-in one.
    /// </summary>
    public static class KnowledgeService
    {
        private static readonly Regex _mappingRegex =
            new(@"Map the layer '(.*?)' to standard layer '(.*?)'\.", RegexOptions.Compiled);

        private static readonly object _initLock = new();
        private static bool _initialized;
        private static string _dbPath = "";

        // ── Path / init ──────────────────────────────────────────────────────────
        private static string GetDbPath()
        {
            // Allows test/mock runs to point at a scratch directory instead of the real
            // learned-mappings data. When not overridden, both BricsAI.Overlay and
            // BricsAI.McpServer resolve to the same shared per-user location so a mapping
            // learned through one app is immediately visible in the other.
            var overrideDir = Environment.GetEnvironmentVariable("BRICSAI_KNOWLEDGE_DIR");
            var basePath = string.IsNullOrWhiteSpace(overrideDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BricsAI")
                : overrideDir;
            return Path.Combine(basePath, "agent_knowledge.db");
        }

        private static SqliteConnection OpenConnection()
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    _dbPath = GetDbPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

                    using var initConn = new SqliteConnection($"Data Source={_dbPath}");
                    initConn.Open();
                    using (var cmd = initConn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            CREATE TABLE IF NOT EXISTS layer_mappings (
                                source_layer TEXT PRIMARY KEY COLLATE NOCASE,
                                target_layer TEXT NOT NULL,
                                updated_at   TEXT NOT NULL
                            );
                            CREATE TABLE IF NOT EXISTS free_form_rules (
                                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                                rule_text  TEXT NOT NULL,
                                created_at TEXT NOT NULL
                            );";
                        cmd.ExecuteNonQuery();
                    }

                    _initialized = true;
                }
            }

            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            return conn;
        }

        private static void UpsertMapping(SqliteConnection conn, string source, string target, string timestamp, SqliteTransaction? transaction = null)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO layer_mappings (source_layer, target_layer, updated_at)
                VALUES ($src, $tgt, $ts)
                ON CONFLICT(source_layer) DO UPDATE SET target_layer = $tgt, updated_at = $ts;";
            cmd.Parameters.AddWithValue("$src", source);
            cmd.Parameters.AddWithValue("$tgt", target);
            cmd.Parameters.AddWithValue("$ts", timestamp);
            cmd.ExecuteNonQuery();
        }

        private static void InsertFreeFormRule(SqliteConnection conn, string ruleText, string timestamp, SqliteTransaction? transaction = null)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO free_form_rules (rule_text, created_at) VALUES ($text, $ts);";
            cmd.Parameters.AddWithValue("$text", ruleText);
            cmd.Parameters.AddWithValue("$ts", timestamp);
            cmd.ExecuteNonQuery();
        }

        // ── Write ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Saves a rule or mapping. If the rule is a layer mapping whose source layer was already
        /// recorded, it's upserted in place (indexed by source_layer) rather than accumulating
        /// duplicates.
        /// </summary>
        public static void SaveLearning(string rule)
        {
            try
            {
                using var conn = OpenConnection();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var match = _mappingRegex.Match(rule);
                if (match.Success)
                {
                    UpsertMapping(conn, match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim(), timestamp);
                }
                else
                {
                    InsertFreeFormRule(conn, rule, timestamp);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving knowledge: {ex.Message}");
            }
        }

        // ── Read ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Returns the full knowledge base as text — every layer mapping and free-form rule,
        /// chronologically interleaved — in the same "[timestamp] Map the layer 'X' to standard
        /// layer 'Y'." / "[timestamp] <rule>" line format the legacy text file used.
        /// </summary>
        public static string GetLearnings()
        {
            try
            {
                var entries = new List<(string Timestamp, string Line)>();
                using var conn = OpenConnection();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT source_layer, target_layer, updated_at FROM layer_mappings;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string ts = reader.GetString(2);
                        entries.Add((ts, $"[{ts}] Map the layer '{reader.GetString(0)}' to standard layer '{reader.GetString(1)}'."));
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT rule_text, created_at FROM free_form_rules;";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string ts = reader.GetString(1);
                        entries.Add((ts, $"[{ts}] {reader.GetString(0)}"));
                    }
                }

                if (entries.Count == 0) return "No user rules learned yet.";

                return string.Join("\n", entries.OrderBy(e => e.Timestamp).Select(e => e.Line));
            }
            catch
            {
                return "Error retrieving knowledge.";
            }
        }

        /// <summary>
        /// Returns only the free-form behavioral rules (e.g. "always run X before Y"), excluding
        /// the (potentially very large) per-layer mapping entries. Layer mappings are already
        /// applied deterministically via apply_layer_mappings/NET:APPLY_LAYER_MAPPINGS and already
        /// filtered out of "unknown layer" detection upstream, so callers that only need behavioral
        /// guidance should prefer this over GetLearnings() to avoid the token cost of dumping every
        /// learned mapping into a prompt.
        /// </summary>
        public static string GetFreeFormRules()
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT rule_text, created_at FROM free_form_rules ORDER BY id ASC;";
                using var reader = cmd.ExecuteReader();

                var lines = new List<string>();
                while (reader.Read())
                    lines.Add($"[{reader.GetString(1)}] {reader.GetString(0)}");

                return lines.Count > 0 ? string.Join("\n", lines) : "No free-form rules learned yet.";
            }
            catch
            {
                return "Error retrieving knowledge.";
            }
        }

        /// <summary>
        /// Returns the layer → target dictionary via an indexed table read instead of a full-file
        /// regex scan.
        /// </summary>
        public static Dictionary<string, string> GetLayerMappingsDictionary()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT source_layer, target_layer FROM layer_mappings;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    dict[reader.GetString(0)] = reader.GetString(1);
            }
            catch { }
            return dict;
        }

        // ── Maintenance ──────────────────────────────────────────────────────────
        /// <summary>
        /// Reclaims disk space freed by prior updates/deletes. Upserts already keep the database
        /// free of duplicate mapping rows, so this is just periodic housekeeping, not a dedupe pass.
        /// </summary>
        public static void CompactFile()
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compacting knowledge database: {ex.Message}");
            }
        }
    }
}
