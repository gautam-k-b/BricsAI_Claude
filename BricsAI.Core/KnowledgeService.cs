using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace BricsAI.Core
{
    public static class KnowledgeService
    {
        private static string GetKnowledgePath()
        {
            // Allows test/mock runs to point at a scratch file instead of the real learned-mappings
            // data next to the executable, so they don't pollute production knowledge.
            var overrideDir = Environment.GetEnvironmentVariable("BRICSAI_KNOWLEDGE_DIR");
            var basePath = string.IsNullOrWhiteSpace(overrideDir) ? AppDomain.CurrentDomain.BaseDirectory : overrideDir;
            var path = Path.Combine(basePath, "agent_knowledge.txt");

            // Touch file if it doesn't exist
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(basePath);
                File.WriteAllText(path, "--- BricsAI Learned Rules & Preferences ---\n\n");
            }
            return path;
        }

        public static void SaveLearning(string rule)
        {
            try
            {
                var path = GetKnowledgePath();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string entry = $"[{timestamp}] {rule}\n";
                File.AppendAllText(path, entry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving knowledge: {ex.Message}");
            }
        }

        public static string GetLearnings()
        {
            try
            {
                var path = GetKnowledgePath();
                if (!File.Exists(path)) return "No user rules learned yet.";
                
                var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("---")).ToList();
                if (!lines.Any()) return "No user rules learned yet.";
                
                return string.Join("\n", lines);
            }
            catch
            {
                return "Error retrieving knowledge.";
            }
        }

        public static Dictionary<string, string> GetLayerMappingsDictionary()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var lines = File.ReadAllLines(GetKnowledgePath());
                var regex = new System.Text.RegularExpressions.Regex(@"Map the layer '(.*?)' to standard layer '(.*?)'\.");
                foreach (var line in lines)
                {
                    var match = regex.Match(line);
                    if (match.Success && match.Groups.Count == 3)
                    {
                        var src = match.Groups[1].Value.Trim();
                        var tgt = match.Groups[2].Value.Trim();
                        dict[src] = tgt; // Latest entry overwrites older ones inherently
                    }
                }
            }
            catch { }
            return dict;
        }
    }
}
