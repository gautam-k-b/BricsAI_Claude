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
    }

    public class MapperAgent : BaseAgent
    {
        public MapperAgent()
        {
            Name = "MapperAgent";
        }

        /// <summary>
        /// Phase 1: Classify all layer names in ONE LLM call.
        /// Returns confident mappings and a list of layer names that need geometry evidence (UNCERTAIN).
        /// </summary>
        public async Task<(List<MappingResult> Confident, List<string> Uncertain, int Tokens, int InputTokens, int OutputTokens)> ClassifyByNameAsync(List<string> layerNames)
        {
            string layerList = string.Join("\n", layerNames.Select((n, i) => $"{i + 1}. {n}"));

            string systemPrompt =
                "You are the BricsCAD Semantic Auto-Mapper Agent.\n" +
                "You will receive a numbered list of unknown CAD layer names. For each layer, classify it into one of the standard A2Z target layers based on its NAME ALONE.\n\n" +
                "STANDARD A2Z TARGET LAYERS:\n" +
                "- Expo_BoothOutline: booth boundary polylines\n" +
                "- Expo_BoothNumber: booth number text labels\n" +
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
                entries.AppendLine($"--- LAYER {i + 1}: {layers[i].LayerName} ---");
                entries.AppendLine(layers[i].Footprint);
            }

            string systemPrompt =
                "You are the BricsCAD Semantic Auto-Mapper Agent.\n" +
                "You will receive multiple unknown CAD layers, each with their geometric footprint data (entity counts, block names, text samples).\n" +
                "For EACH layer, deduce which standard A2Z target layer it belongs to.\n\n" +
                "STANDARD A2Z TARGET LAYERS AND RULES:\n" +
                "1. Expo_View2: Electrical ports, power drops, building utilities, fire exits, fire hoses, keep-clear demarcations.\n" +
                "2. Expo_Column: Column-like structural supports and pillars.\n" +
                "3. Expo_BoothOutline: Booth boundary polylines/rectangles.\n" +
                "4. Expo_BoothNumber: Booth number text labels.\n" +
                "5. Expo_Building: Walls, partitions, doors, stairs, airwalls, permanent fixtures, railings.\n" +
                "6. Expo_Markings: Entrances, washroom labels, show titles, hall names, general non-booth text.\n" +
                "7. Expo_NES: Non-exhibiting spaces -- enclosed boxes with text like service areas, restrooms.\n\n" +
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
                            LispCode = $"NET:LEARN_LAYER_MAPPING:{src}:{tgt}"
                        });
                    }
                }
            }
            catch { }

            return (mappings, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }

        /// <summary>
        /// Legacy single-layer method — kept for fallback use.
        /// </summary>
        public async Task<(string ActionPlan, string Reason, int Tokens, int InputTokens, int OutputTokens)> DeduceLayerMappingAsync(string layerName, string geometricFootprint)
        {
            string systemPrompt = $@"You are the BricsCAD Semantic Auto-Mapper Agent.
Your sole purpose is to act as a human structural CAD drafter. You will be provided the name of an unknown vendor layer and a text summary of its geometric contents (its 'Geometric Footprint'). 
You must analyze the types of entities, block names, and text values within the footprint to deduce which standard A2Z layer the entities belong to.

ONCE YOU MAKE A DECISION, YOU MUST OUTPUT A JSON OBJECT with a 'reason' field and a 'tool_calls' array.

CRITICAL STRICT HUMAN-LEVEL ROUTING RULES:
You MUST map the unknown layer to one of these standardized A2Z targets based on the following precise definitions:
1. Expo_View2: Electrical ports, power drops, building utilities, fire exits, fire hoses, 'keep clear' demarcations.
2. Expo_Column: Column-like structural supports and pillars.
3. Expo_BoothOutline: Booth outlines. (NOTE: If the source layer is ALREADY named exactly Expo_BoothOutline, you must DO NOTHING and skip it).
4. Expo_BoothNumber: Booth numbers/labels. (NOTE: If the source layer is ALREADY named exactly Expo_BoothNumber, you must DO NOTHING and skip it).
5. Expo_Building: Objects which make up the physical building architecture (walls, partitions, doors, stairs, airwalls, permanent fixtures, railings).
6. Expo_Markings: Text objects representing entrances, washroom labels (Male, Female, Man, Woman), Show titles, hall names, and general non-booth text.
7. Expo_NES: Non-broken boxes with text inside that look like Non-Exhibiting Spaces (NES).

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

            string prompt = $"UNKNOWN LAYER NAME: {layerName}\nGEOMETRIC FOOTPRINT:\n{geometricFootprint}\n\nBased on the rules, deduce the target layer and generate the strict JSON response.";

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
