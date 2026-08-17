using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class TableReviewResponse
    {
        public string Intent { get; set; } = "QUESTION";
        public List<int> Indexes { get; set; } = new List<int>();
        public string RuleText { get; set; } = "";
    }

    public class MappingReviewAgent : BaseAgent
    {
        public MappingReviewAgent()
        {
            Name = "MappingReviewAgent";
        }

        /// <summary>
        /// Classifies a free-form reply against the full numbered table of mapping proposals shown
        /// to the user. One call handles index-based include/exclude ("include 1,3,5", "exclude 2"),
        /// a mid-review memorize instruction ("remember X always maps to Y"), blanket confirmation,
        /// a layer-visibility action request, a pure question, or an abort.
        /// </summary>
        public async Task<(TableReviewResponse Response, int Tokens, int InputTokens, int OutputTokens)> ClassifyTableReviewResponseAsync(
            string userMessage,
            List<(string Source, string Target)> proposals,
            Dictionary<string, string> confidenceByLayer,
            Dictionary<string, string> reasonByLayer,
            HashSet<int> alreadyIncluded,
            HashSet<int> alreadyExcluded)
        {
            var table = new StringBuilder();
            for (int i = 0; i < proposals.Count; i++)
            {
                int idx = i + 1;
                string status = alreadyIncluded.Contains(idx) ? "INCLUDED" : alreadyExcluded.Contains(idx) ? "EXCLUDED" : "PENDING";
                string confidence = confidenceByLayer.TryGetValue(proposals[i].Source, out var c) ? c : "Low";
                string reason = reasonByLayer.TryGetValue(proposals[i].Source, out var r) && !string.IsNullOrWhiteSpace(r) ? r : "no stated reason";
                table.AppendLine($"{idx}. {proposals[i].Source} -> {proposals[i].Target} (Confidence: {confidence}, Reason: {reason}, Status: {status})");
            }

            string systemPrompt = $@"You are the Mapping Review Classifier for BricsAI.
The system has shown the user a numbered table of proposed CAD layer mappings and is waiting for their reply.

[CURRENT PROPOSALS]
{table}

Your job is to read the user's free-form reply and classify it into exactly ONE of these intents:

[KEYWORDS]
INCLUDE: the user wants specific numbered rows accepted (saved). Examples: 'include 1,3,5', 'accept 2 and 4', 'yes to 1', 'keep 3'.
EXCLUDE: the user wants specific numbered rows rejected (not saved). Examples: 'exclude 2,4', 'skip 1', 'reject 3', 'no to 5'.
MEMORIZE: the user is stating a standalone rule or mapping to remember for the future, independent of the numbered rows shown — NOT a response about the current table rows. Examples: 'remember that VendorLayerX always maps to Expo_Building', 'always skip layers starting with TEMP_', 'add a mapping: Layer99 to Expo_Column'. Put the plain-English rule (as a full sentence starting with 'Map the layer ...' for a layer mapping, or the free-form rule text otherwise) in ruleText.
CONFIRM_ALL: the user wants every remaining PENDING row accepted and the review finished. Examples: 'looks good', 'confirm all', 'accept the rest', 'that's fine, proceed', 'done'.
ACTION: the user is asking for a BricsCAD layer-visibility action (show/hide/isolate layers), not a decision about rows. Examples: 'show only the ones mapped to Expo_Building', 'hide everything else', 'isolate row 3's layer'.
QUESTION: the user is asking a pure informational question about the table, not deciding anything. Examples: 'what does row 2 map to?', 'how many are low confidence?', 'why is row 4 mapped that way?'.
ABORT: the user wants to cancel the whole review. Examples: 'stop', 'cancel', 'abort', 'nevermind'.

RULES:
- For INCLUDE/EXCLUDE, put the 1-based row numbers mentioned in indexes. Numbers can be referenced by digit or spelled out; comma/space separated.
- A single reply may only carry ONE intent — if the user both decides some rows AND states a memorize rule in the same message, prefer MEMORIZE only when the rule is clearly a standalone/general rule unrelated to a specific row number; otherwise prefer INCLUDE/EXCLUDE.
- Do not re-decide rows that are already INCLUDED or EXCLUDED unless the user explicitly changes their mind about that row.

OUTPUT FORMAT: strict JSON only, no markdown, no explanation:
{{ ""intent"": ""INCLUDE|EXCLUDE|MEMORIZE|CONFIRM_ALL|ACTION|QUESTION|ABORT"", ""indexes"": [1,3], ""ruleText"": """" }}";

            string prompt = $"USER REPLY: {userMessage}";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);
            BricsAI.Core.LoggerService.LogAgentPrompt("MappingTableClassifier", result.Content);

            var response = new TableReviewResponse();
            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                var root = doc.RootElement;
                if (root.TryGetProperty("intent", out var intentEl))
                    response.Intent = (intentEl.GetString() ?? "QUESTION").Trim().ToUpper();
                if (root.TryGetProperty("indexes", out var idxEl) && idxEl.ValueKind == JsonValueKind.Array)
                    response.Indexes = idxEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()).ToList();
                if (root.TryGetProperty("ruleText", out var ruleEl))
                    response.RuleText = ruleEl.GetString() ?? "";
            }
            catch { }

            return (response, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }

        /// <summary>
        /// Answers a pure informational question about the pending mappings without executing anything.
        /// </summary>
        public async Task<string> AnswerMappingQuestionAsync(string currentProposals, string userQuestion)
        {
            string systemPrompt = @"You are the BricsCAD Mapping Review Assistant.
The system is paused for human review of auto-generated layer mapping proposals.
The user has asked a pure informational question about the mappings.

YOUR ONLY JOB: Answer the user's question conversationally in plain English based on the proposed mappings provided.
- Do NOT output any JSON or commands.
- Do NOT tell the user to confirm or proceed — just answer their question.
- If they ask which layers map to a specific target (e.g., 'Expo_Building'), list only those source layers.
- Keep your answer concise and friendly.";

            string prompt = $"CURRENT PROPOSED MAPPINGS:\n{currentProposals}\n\nUSER QUESTION:\n{userQuestion}\n\nAnswer the question based on the mappings above.";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: false);
            return result.Content;
        }

        /// <summary>
        /// Translates an action-phrased request (e.g., "show only Expo_Building layers") into a BricsCAD tool_calls JSON plan.
        /// The returned plan isolates/toggles/hides layers as requested, using only the layers in the pending mapping proposals.
        /// </summary>
        public async Task<(string ActionPlan, int Tokens, int InputTokens, int OutputTokens)> BuildLayerActionPlanAsync(string currentProposals, string userRequest)
        {
            string systemPrompt = @"You are the BricsCAD Layer Action Agent.
The system is paused during a mapping review. The user has asked you to perform a specific layer visibility action in BricsCAD (e.g., isolate, hide, show layers).

You have access to the following BricsCAD LISP commands via NET tool_calls:
- NET:ISOLATE_LAYERS:<LayerName1>,<LayerName2>,...  → Hides ALL layers except the listed ones (turn OFF all, turn ON listed). Layer 0 is always kept visible.
- NET:SHOW_LAYER:<LayerName>  → Makes a single layer visible (ON).
- NET:HIDE_LAYER:<LayerName>  → Makes a single layer invisible (OFF).
- NET:SHOW_ALL_LAYERS  → Makes all layers visible.

INSTRUCTIONS:
1. Read the user's request carefully. Figure out which layers they want visible/hidden.
2. Use the proposed mappings to identify the SOURCE layers that map to their requested target (e.g., layers that map to 'Expo_Building').
3. Output a valid JSON tool_calls plan using the commands above.
4. Layer 0 must always remain visible — never hide it.
5. Output ONLY valid JSON. No explanations, no markdown.

JSON Schema:
{
  ""tool_calls"": [
    {
      ""command_name"": ""Layer Action"",
      ""lisp_code"": ""NET:ISOLATE_LAYERS:LayerA,LayerB""
    }
  ]
}";

            string prompt = $"CURRENT PROPOSED MAPPINGS (source → target):\n{currentProposals}\n\nUSER REQUEST:\n{userRequest}\n\nGenerate the BricsCAD tool_calls JSON to fulfill this layer action request. Only use layers mentioned in the proposed mappings above.";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);
            return (result.Content, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }
    }
}
