using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BricsAI.Core;
using BricsAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace BricsAI.McpServer.Tools
{
    [McpServerToolType]
    public static class MemoryTools
    {
        public static readonly HashSet<string> StandardA2zLayers = new(StringComparer.OrdinalIgnoreCase)
        {
            "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building",
            "Expo_Markings", "Expo_View2", "Expo_Column", "Expo_NES", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
        };

        [McpServerTool(Name = "learn_rule"), Description("Saves a free-form user preference or workflow rule to agent_knowledge.txt for future sessions.")]
        public static Task<string> LearnRule(ComClient comClient, StaComHost sta, [Description("The rule or preference to remember, in plain English.")] string rule)
            => ToolExec.RunAsync(comClient, sta, $"NET:LEARN_RULE:{rule}");

        [McpServerTool(Name = "get_learned_mappings"), Description("Returns the full contents of agent_knowledge.txt — every previously learned layer mapping and rule. Read this before proposing new mappings so you don't re-ask about layers already handled.")]
        public static string GetLearnedMappings() => KnowledgeService.GetLearnings();

        [McpServerTool(Name = "get_unmapped_layers"), Description(
            "Deterministically returns every layer name in the drawing that is NOT a standard A2Z layer, NOT already a learned mapping source, " +
            "and NOT already retired with a 'Deleted_' prefix. Call this before apply_layer_mappings to see what still needs to be classified.")]
        public static async Task<string> GetUnmappedLayers(ComClient comClient, StaComHost sta)
        {
            string raw = await ToolExec.RunAsync(comClient, sta, "NET:GET_LAYERS");

            string payload = raw;
            const string marker = "Layers found:";
            int idx = payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) payload = payload[(idx + marker.Length)..];

            var knownMappings = KnowledgeService.GetLayerMappingsDictionary();

            var unmapped = payload
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l)
                            && !StandardA2zLayers.Contains(l)
                            && !knownMappings.ContainsKey(l)
                            && !l.StartsWith("Deleted_", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unmapped.Count == 0) return "No unmapped layers — every layer is either standard, already learned, or already retired.";
            return $"{unmapped.Count} unmapped layer(s):\n" + string.Join("\n", unmapped);
        }
    }
}
