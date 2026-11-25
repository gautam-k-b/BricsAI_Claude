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
            "Expo_Markings", "Expo_View2", "Expo_Column", "Expo_NES", "Expo_ImageMarkings", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
        };

        [McpServerTool(Name = "learn_rule"), Description("Saves a free-form user preference or workflow rule to the knowledge database for future sessions.")]
        public static Task<string> LearnRule(ComClient comClient, StaComHost sta, [Description("The rule or preference to remember, in plain English.")] string rule)
            => ToolExec.RunAsync(comClient, sta, $"NET:LEARN_RULE:{rule}");

        [McpServerTool(Name = "get_layers_for_review"), Description(
            "Returns the COMPLETE review list for a proofing run, mirroring the BricsAI Overlay mapping grid: every non-frozen layer in the drawing " +
            "except layer 0, Defpoints, the booth layers and the other standard Expo_ layers. Layers that already have a learned mapping are listed " +
            "first as LEARNED (High confidence, from memory, shown with their target); every other layer is listed as NEW and must be classified by you. " +
            "No layer may be skipped. Retired Deleted_ layers from earlier runs are only counted. Read-only.")]
        public static async Task<string> GetLayersForReview(ComClient comClient, StaComHost sta)
        {
            string raw = await ToolExec.RunAsync(comClient, sta, "NET:GET_LAYERS");
            string payload = raw;
            const string marker = "Layers found:";
            int idx = payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) payload = payload[(idx + marker.Length)..];

            var known = new Dictionary<string, string>(KnowledgeService.GetLayerMappingsDictionary(), StringComparer.OrdinalIgnoreCase);

            var layers = payload
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !StandardA2zLayers.Contains(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var retired = layers.Where(l => l.StartsWith("Deleted_", StringComparison.OrdinalIgnoreCase)).ToList();
            var candidates = layers.Except(retired, StringComparer.OrdinalIgnoreCase).ToList();
            var learned = candidates.Where(known.ContainsKey).ToList();
            var fresh = candidates.Where(l => !known.ContainsKey(l)).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"REVIEW LIST: {candidates.Count} layer(s) = {learned.Count} learned (memory) + {fresh.Count} new. Frozen, standard A2Z and booth layers are excluded.");
            sb.AppendLine();
            sb.AppendLine($"LEARNED - from memory, High confidence ({learned.Count}):");
            foreach (var l in learned) sb.AppendLine($"{l} -> {known[l]}");
            sb.AppendLine();
            sb.AppendLine($"NEW - not in memory, classify each one ({fresh.Count}):");
            foreach (var l in fresh) sb.AppendLine(l);
            sb.AppendLine();
            sb.AppendLine($"Retired Deleted_ layers not listed: {retired.Count}");
            return sb.ToString();
        }

        [McpServerTool(Name = "set_mapping_review_filter"), Description(
            "Restricts apply_layer_mappings / run_full_proofing to ONLY the source layers the user approved in the review table. " +
            "Without it, every learned mapping in memory is applied to every matching layer, including ones the user excluded. " +
            "Pass the approved source layer names (learned and new) as a comma-separated list. An empty list means apply nothing. " +
            "run_full_proofing clears this filter automatically when it finishes.")]
        public static string SetMappingReviewFilter([Description("Comma-separated approved source layer names.")] string approvedSourceLayers)
        {
            var approved = (approvedSourceLayers ?? "")
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            KnowledgeService.SetApplyFilter(approved);
            return $"Mapping review filter set: {approved.Count} approved source layer(s) will be applied; all other learned mappings are skipped this run.";
        }

        [McpServerTool(Name = "clear_mapping_review_filter"), Description("Removes the mapping review filter so every learned mapping is applied again.")]
        public static string ClearMappingReviewFilter()
        {
            KnowledgeService.ClearApplyFilter();
            return "Mapping review filter cleared.";
        }

        [McpServerTool(Name = "unlearn_layer_mapping"), Description("Removes the learned mapping for a source layer (the equivalent of choosing 'None' in the Overlay review grid). Only call this when the user asks to forget or skip a mapping.")]
        public static string UnlearnLayerMapping([Description("The source (vendor) layer whose learned mapping should be removed.")] string sourceLayer)
        {
            KnowledgeService.RemoveMapping(sourceLayer);
            return $"Removed any learned mapping for '{sourceLayer}'.";
        }

        [McpServerTool(Name = "get_learned_mappings"), Description("Returns the full contents of the knowledge database — every previously learned layer mapping and rule. Read this before proposing new mappings so you don't re-ask about layers already handled.")]
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
