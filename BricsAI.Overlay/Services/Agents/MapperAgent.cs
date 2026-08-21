using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class MappingResult
    {
        public string SourceLayer { get; set; } = "";
        public string TargetLayer { get; set; } = "";
        public string Reason { get; set; } = "";
        public string LispCode { get; set; } = "";
        /// <summary>
        /// "High" for layers classified by name alone (Phase 1), "Low" for layers that
        /// needed geometry evidence because the name alone was ambiguous (Phase 2).
        /// </summary>
        public string Confidence { get; set; } = "High";
    }

    public class MapperAgent : BaseAgent
    {
        public MapperAgent()
        {
            Name = "MapperAgent";
        }

        // Extracts the semantic segment from a CAD xref-qualified layer name.
        // Example: "McCormick Place$0$Level 3 FINAL$0$Bldg_Wall" → "Bldg_Wall"
        // Returns the original name unchanged when no "$0$" separator is present.
        private static string ExtractSemanticHint(string layerName)
        {
            int idx = layerName.LastIndexOf("$0$", StringComparison.Ordinal);
            return idx >= 0 ? layerName.Substring(idx + 3) : layerName;
        }

        /// <summary>
        /// Phase 1: Classify layer names — batches at 40 layers per LLM call to avoid token-limit failures.
        /// Returns confident mappings and a list of layer names that need geometry evidence (UNCERTAIN).
        /// </summary>
        public async Task<(List<MappingResult> Confident, List<string> Uncertain, int Tokens, int InputTokens, int OutputTokens)> ClassifyByNameAsync(List<string> layerNames)
        {
            const int batchSize = 40;
            var allConfident = new List<MappingResult>();
            var allUncertain = new List<string>();
            int totalTokens = 0, totalInput = 0, totalOutput = 0;

            for (int batchStart = 0; batchStart < layerNames.Count; batchStart += batchSize)
            {
                var batch = layerNames.Skip(batchStart).Take(batchSize).ToList();
                var (c, u, t, inp, outp) = await ClassifySingleBatchAsync(batch);
                allConfident.AddRange(c);
                allUncertain.AddRange(u);
                totalTokens += t;
                totalInput += inp;
                totalOutput += outp;
            }

            return (allConfident, allUncertain, totalTokens, totalInput, totalOutput);
        }

        private async Task<(List<MappingResult> Confident, List<string> Uncertain, int Tokens, int InputTokens, int OutputTokens)> ClassifySingleBatchAsync(List<string> layerNames)
        {
            string layerList = string.Join("\n", layerNames.Select((n, i) =>
            {
                string hint = ExtractSemanticHint(n);
                return hint != n ? $"{i + 1}. {n}  [semantic type: {hint}]" : $"{i + 1}. {n}";
            }));

            string systemPrompt =
                "You are the BricsCAD Semantic Auto-Mapper Agent.\n" +
                "You will receive a numbered list of unknown CAD layer names. For each layer, classify it into one of the standard A2Z target layers based on its NAME ALONE.\n\n" +
                "XREF LAYER NAME CONVENTION:\n" +
                "CAD drawings often attach external references (xrefs), which prefix every layer name with the host drawing name using '$0$' as a separator. " +
                "The segment after the LAST '$0$' is the true semantic layer type and is your primary classification signal. " +
                "A '[semantic type: ...]' hint is shown beside each such name — treat it exactly like a plain layer name. " +
                "Example: 'McCormick Place_West Building_ Level 3$0$Bldg_Wall' → semantic type 'Bldg_Wall' → Expo_Building.\n\n" +
                "STANDARD A2Z TARGET LAYERS:\n" +
                "- Expo_BoothOutline: booth boundary polylines\n" +
                "- Expo_BoothNumber: booth number text labels\n" +
                "- Expo_MaxBoothOutline: oversized/max-footprint booth outlines\n" +
                "- Expo_MaxBoothNumber: text labels for max-footprint booth numbers\n" +
                "- Expo_Building: walls, partitions, doors, stairs, railings, permanent fixtures\n" +
                "- Expo_Column: structural columns and pillars\n" +
                "- Expo_Markings: entrance/exit labels, washroom labels, hall names, title blocks, annotations\n" +
                "- Expo_View2: power drops, electrical ports, fire exits, utilities, viewports\n" +
                "- Expo_NES: non-exhibiting spaces (service areas, restrooms)\n\n" +
                "CONFIDENCE RULES:\n" +
                "- If the layer name strongly suggests a target (e.g. 'A-WALL', 'G-COLS', 'BOOTH-NUM', 'E-PWR'), classify it as CONFIDENT.\n" +
                "- If the layer name is ambiguous (numeric codes, generic names like 'MISC', 'LAYER1', single letters), classify as UNCERTAIN -- it needs geometry evidence.\n\n" +
                "OUTPUT FORMAT: A JSON object with a \"mappings\" array. Each entry MUST have: \"source\", \"target\" (or \"UNCERTAIN\"), \"reason\" (max 15 words, only for confident ones).\n" +
                "{ \"mappings\": [ { \"source\": \"A-WALL-INT\", \"target\": \"Expo_Building\", \"reason\": \"Name contains WALL indicating interior wall geometry.\" }, { \"source\": \"LAYER1\", \"target\": \"UNCERTAIN\", \"reason\": \"\" } ] }\n\n" +
                "Output ONLY valid JSON matching this schema. No markdown, no explanation.";

            string prompt = $"LAYER NAMES TO CLASSIFY:\n{layerList}";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);

            var confident = new List<MappingResult>();
            var uncertain = new List<string>();

            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                var array = doc.RootElement.TryGetProperty("mappings", out var m)
                    ? m
                    : doc.RootElement;

                foreach (var item in array.EnumerateArray())
                {
                    string src = item.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                    string tgt = item.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                    string rsn = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(src)) continue;

                    if (tgt == "UNCERTAIN" || string.IsNullOrEmpty(tgt))
                    {
                        uncertain.Add(src);
                    }
                    else
                    {
                        confident.Add(new MappingResult
                        {
                            SourceLayer = src,
                            TargetLayer = tgt,
                            Reason = rsn,
                            LispCode = $"NET:LEARN_LAYER_MAPPING:{src}:{tgt}"
                        });
                    }
                }
            }
            catch { }

            return (confident, uncertain, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }

        /// <summary>
        /// Phase 2: Classify uncertain layers in ONE LLM call using their geometry footprints.
        /// </summary>
        public async Task<(List<MappingResult> Mappings, int Tokens, int InputTokens, int OutputTokens)> BatchDeduceByGeometryAsync(List<(string LayerName, string Footprint)> layers)
        {
            if (layers.Count == 0)
                return (new List<MappingResult>(), 0, 0, 0);

            var entries = new StringBuilder();
            for (int i = 0; i < layers.Count; i++)
            {
                string hint = ExtractSemanticHint(layers[i].LayerName);
                string header = hint != layers[i].LayerName
                    ? $"--- LAYER {i + 1}: {layers[i].LayerName}  [semantic type: {hint}] ---"
                    : $"--- LAYER {i + 1}: {layers[i].LayerName} ---";
                entries.AppendLine(header);
                entries.AppendLine(layers[i].Footprint);
            }

            string systemPrompt =
                "You are the BricsCAD Semantic Auto-Mapper Agent.\n" +
                "You will receive multiple unknown CAD layers, each with their geometric footprint data (entity counts, block names, text samples).\n" +
                "For EACH layer, deduce which standard A2Z target layer it belongs to.\n\n" +
                "XREF LAYER NAME CONVENTION:\n" +
                "Layer names may contain '$0$' separators (CAD xref convention). The segment after the LAST '$0$' is the semantic layer type. " +
                "A '[semantic type: ...]' hint is shown in each layer header — treat it as the primary name signal alongside the geometry.\n\n" +
                "STANDARD A2Z TARGET LAYERS AND RULES:\n" +
                "1. Expo_View2: Electrical ports, power drops, building utilities, fire exits, fire hoses, keep-clear demarcations.\n" +
                "2. Expo_Column: Column-like structural supports and pillars.\n" +
                "3. Expo_BoothOutline: Booth boundary polylines/rectangles.\n" +
                "4. Expo_BoothNumber: Booth number text labels.\n" +
                "5. Expo_MaxBoothOutline: Oversized/max-footprint booth outlines.\n" +
                "6. Expo_MaxBoothNumber: Text labels for max-footprint booth numbers.\n" +
                "7. Expo_Building: Walls, partitions, doors, stairs, airwalls, permanent fixtures, railings.\n" +
                "8. Expo_Markings: Entrances, washroom labels, show titles, hall names, general non-booth text.\n" +
                "9. Expo_NES: Non-exhibiting spaces -- enclosed boxes with text like service areas, restrooms.\n\n" +
                "OUTPUT FORMAT: A JSON object with a \"mappings\" array. Each entry MUST have: \"source\", \"target\", \"reason\" (one plain-English sentence, max 20 words, mentioning key evidence).\n" +
                "{ \"mappings\": [ { \"source\": \"LAYER_A\", \"target\": \"Expo_Building\", \"reason\": \"312 lines and polylines typical of wall and partition geometry.\" } ] }\n\n" +
                "Output ONLY valid JSON matching this schema. No markdown, no explanation.";

            string prompt = $"LAYERS TO CLASSIFY:\n{entries}";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);

            var mappings = new List<MappingResult>();
            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                var array = doc.RootElement.TryGetProperty("mappings", out var m)
                    ? m
                    : doc.RootElement;

                foreach (var item in array.EnumerateArray())
                {
                    string src = item.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                    string tgt = item.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                    string rsn = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(tgt) && tgt != "UNCERTAIN")
                    {
                        mappings.Add(new MappingResult
                        {
                            SourceLayer = src,
                            TargetLayer = tgt,
                            Reason = rsn,
                            LispCode = $"NET:LEARN_LAYER_MAPPING:{src}:{tgt}",
                            Confidence = "Low"
                        });
                    }
                }
            }
            catch { }

            return (mappings, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }

        /// <summary>
        /// Phase 3: Visually verify low-confidence mappings using exported layer snapshot images.
        /// For each layer, Claude is shown the actual rendered image alongside the proposed mapping
        /// and asked to confirm or correct it.
        /// </summary>
        public async Task<(List<MappingResult> Verified, int Tokens, int InputTokens, int OutputTokens)> VisuallyVerifyLowConfidenceAsync(
            List<(string LayerName, string ProposedTarget, string SnapshotPath)> layers)
        {
            if (layers.Count == 0)
                return (new List<MappingResult>(), 0, 0, 0);

            string systemPrompt =
                "You are the BricsCAD Semantic Auto-Mapper Agent performing visual verification.\n" +
                "You will receive a CAD layer snapshot image followed by the layer name and a proposed standard mapping.\n" +
                "Examine the image carefully and decide: does the visual content confirm the proposed mapping, or does it suggest a different target?\n\n" +
                "STANDARD A2Z TARGET LAYERS:\n" +
                "- Expo_Building: walls, partitions, doors, stairs, railings, permanent fixtures\n" +
                "- Expo_Column: structural columns and pillars\n" +
                "- Expo_BoothOutline: booth boundary polylines/rectangles\n" +
                "- Expo_BoothNumber: booth number text labels\n" +
                "- Expo_MaxBoothOutline: oversized/max-footprint booth outlines\n" +
                "- Expo_MaxBoothNumber: text labels for max-footprint booth numbers\n" +
                "- Expo_Markings: entrance/exit labels, washroom labels, hall names, annotations\n" +
                "- Expo_View2: power drops, electrical ports, fire exits, utilities\n" +
                "- Expo_NES: non-exhibiting spaces (service areas, restrooms)\n\n" +
                "OUTPUT FORMAT: A JSON object with a \"mappings\" array. Each entry MUST have: \"source\", \"target\", \"confirmed\" (true/false), \"reason\" (one sentence, max 20 words).\n" +
                "{ \"mappings\": [ { \"source\": \"LAYER_A\", \"target\": \"Expo_Building\", \"confirmed\": true, \"reason\": \"Image shows continuous wall lines forming building perimeter.\" } ] }\n\n" +
                "Output ONLY valid JSON. No markdown, no explanation.";

            var verified = new List<MappingResult>();
            int totalTokens = 0, totalInput = 0, totalOutput = 0;

            // Process each layer individually so each image-text pair is unambiguous
            foreach (var (layerName, proposedTarget, snapshotPath) in layers)
            {
                string hint = ExtractSemanticHint(layerName);
                string hintClause = hint != layerName ? $"  [semantic type: {hint}]" : "";
                string userPrompt =
                    $"LAYER NAME: {layerName}{hintClause}\n" +
                    $"PROPOSED MAPPING: {proposedTarget}\n\n" +
                    $"The image above shows all entities on this layer. Does the visual content confirm the proposed mapping, or should it be corrected?\n" +
                    $"Respond with JSON using the source value \"{layerName}\".";

                var result = await CallModelWithImagesAsync(systemPrompt, userPrompt, new[] { snapshotPath }, expectJson: true);
                totalTokens += result.TotalTokens;
                totalInput  += result.InputTokens;
                totalOutput += result.OutputTokens;

                try
                {
                    using var doc = JsonDocument.Parse(result.Content);
                    var array = doc.RootElement.TryGetProperty("mappings", out var m) ? m : doc.RootElement;
                    foreach (var item in array.EnumerateArray())
                    {
                        string src = item.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                        string tgt = item.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                        string rsn = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                        bool confirmed = item.TryGetProperty("confirmed", out var c) && c.GetBoolean();

                        if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(tgt))
                        {
                            verified.Add(new MappingResult
                            {
                                SourceLayer = src,
                                TargetLayer = tgt,
                                Reason = rsn,
                                LispCode = $"NET:LEARN_LAYER_MAPPING:{src}:{tgt}",
                                Confidence = confirmed ? "High" : "Low"
                            });
                        }
                    }
                }
                catch { }
            }

            return (verified, totalTokens, totalInput, totalOutput);
        }

        /// <summary>
        /// Legacy single-layer method — kept for fallback use.
        /// </summary>
        public async Task<(string ActionPlan, string Reason, int Tokens, int InputTokens, int OutputTokens)> DeduceLayerMappingAsync(string layerName, string geometricFootprint)
        {
            string semanticHint = ExtractSemanticHint(layerName);
            string layerNameWithHint = semanticHint != layerName
                ? $"{layerName}  [semantic type: {semanticHint}]"
                : layerName;

            string systemPrompt = $@"You are the BricsCAD Semantic Auto-Mapper Agent.
Your sole purpose is to act as a human structural CAD drafter. You will be provided the name of an unknown vendor layer and a text summary of its geometric contents (its 'Geometric Footprint').
You must analyze the types of entities, block names, and text values within the footprint to deduce which standard A2Z layer the entities belong to.

XREF LAYER NAME CONVENTION:
Layer names may contain '$0$' separators (CAD xref convention: <HostDrawing>$0$<SemanticType>). The segment after the LAST '$0$' is the true semantic layer type — treat it as the primary name signal. A '[semantic type: ...]' hint is included in the layer name below for your convenience.

ONCE YOU MAKE A DECISION, YOU MUST OUTPUT A JSON OBJECT with a 'reason' field and a 'tool_calls' array.

CRITICAL STRICT HUMAN-LEVEL ROUTING RULES:
You MUST map the unknown layer to one of these standardized A2Z targets based on the following precise definitions:
1. Expo_View2: Electrical ports, power drops, building utilities, fire exits, fire hoses, 'keep clear' demarcations.
2. Expo_Column: Column-like structural supports and pillars.
3. Expo_BoothOutline: Booth outlines. (NOTE: If the source layer is ALREADY named exactly Expo_BoothOutline, you must DO NOTHING and skip it).
4. Expo_BoothNumber: Booth numbers/labels. (NOTE: If the source layer is ALREADY named exactly Expo_BoothNumber, you must DO NOTHING and skip it).
5. Expo_MaxBoothOutline: Oversized/max-footprint booth outlines. (NOTE: If the source layer is ALREADY named exactly Expo_MaxBoothOutline, you must DO NOTHING and skip it).
6. Expo_MaxBoothNumber: Text labels for max-footprint booth numbers. (NOTE: If the source layer is ALREADY named exactly Expo_MaxBoothNumber, you must DO NOTHING and skip it).
7. Expo_Building: Objects which make up the physical building architecture (walls, partitions, doors, stairs, airwalls, permanent fixtures, railings).
8. Expo_Markings: Text objects representing entrances, washroom labels (Male, Female, Man, Woman), Show titles, hall names, and general non-booth text.
9. Expo_NES: Non-broken boxes with text inside that look like Non-Exhibiting Spaces (NES).

If the layer consists primarily of raw, unnamed rectangles or lines but the layer name itself hints at booths (e.g. 'l1xxxx', 'show_exhibit'), guess `Expo_BoothOutline`.

JSON Schema:
{{
  ""reason"": ""One short plain-English sentence explaining WHY you chose this target layer, mentioning the key evidence. Maximum 20 words."",
  ""tool_calls"": [
    {{
      ""command_name"": ""Semantic Mapping"",
      ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:<SourceLayer>:<TargetLayer>""
    }}
  ]
}}

YOU MUST ONLY OUTPUT VALID JSON MATCHING THIS SCHEMA EXACTLY. DO NOT OUTPUT MARKDOWN, TEXT, OR EXPLANATIONS.
";

            string prompt = $"UNKNOWN LAYER NAME: {layerNameWithHint}\nGEOMETRIC FOOTPRINT:\n{geometricFootprint}\n\nBased on the rules, deduce the target layer and generate the strict JSON response.";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);

            string reason = "";
            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                if (doc.RootElement.TryGetProperty("reason", out var r))
                    reason = r.GetString() ?? "";
            }
            catch { }

            return (result.Content, reason, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }
    }
}
