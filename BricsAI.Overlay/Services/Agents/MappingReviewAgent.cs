using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class MappingReviewAgent : BaseAgent
    {
        public MappingReviewAgent()
        {
            Name = "MappingReviewAgent";
        }

        /// <summary>
        /// Classifies user response to a single mapping proposal.
        /// Returns: ACCEPT, SKIP, or ABORT
        /// </summary>
        public async Task<string> ClassifySingleMappingResponseAsync(string userResponse, string sourceLayer, string proposedTarget)
        {
            string systemPrompt = $@"You are the Mapping Review Classifier for BricsAI.
The system has shown the user a SINGLE layer mapping proposal and is waiting for their response.

The proposed mapping is: Map '{sourceLayer}' to '{proposedTarget}'

Your ONLY job is to read the user's response and classify their intent into exactly ONE of the following three keywords:

[KEYWORDS]
ACCEPT
SKIP
ABORT

[DEFINITIONS]
ACCEPT: The user agrees with this specific mapping and wants to save it and move to the next. Examples: 'yes', 'looks good', 'that's correct', 'sure', 'go ahead', 'perfect', 'agree', 'yep', 'ok', 'fine', 'proceed', 'next', 'accepted', 'good mapping'.
SKIP: The user wants to skip THIS mapping only (do not save it) and move to the next proposal. They still want to continue reviewing other mappings. Examples: 'no', 'skip', 'skip this', 'not needed', 'not this one', 'next please', 'wrong', 'that's not right', 'try another', 'pass', 'ignore this one'.
ABORT: The user wants to stop reviewing mappings entirely and stop the whole proofing process. They are cancelling everything, not just this mapping. Examples: 'stop', 'cancel', 'abort', 'nevermind', 'forget it', 'don't continue', 'quit', 'stop reviewing', 'no more mappings', 'cancel everything'.

CRITICAL RULE: Only return ABORT if the user explicitly says stop/cancel/abort/quit. SKIP is for skipping just this one mapping.

You MUST output strictly one of these three exact words. Do not output any other text.";

            string prompt = $"USER RESPONSE: {userResponse}";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: false);
            return result.Content.Trim().ToUpper();
        }

        /// <summary>
        /// Validates a single mapping proposal to determine if it should be presented to user.
        /// Returns true if valid and should be shown.
        /// </summary>
        public async Task<bool> ValidateMappingProposalAsync(string sourceLayer, string proposedTarget)
        {
            // Basic validation rules
            var standardA2zLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building",
                "Expo_Markings", "Expo_View2", "Expo_Column", "Expo_NES", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            };

            // Ensure proposed target is a valid standard layer
            if (!standardA2zLayers.Contains(proposedTarget))
                return false;

            // Ensure source is not already a standard layer
            if (standardA2zLayers.Contains(sourceLayer))
                return false;

            // Ensure source and target are different
            if (sourceLayer.Equals(proposedTarget, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public async Task<(string UpdatedMappings, int Tokens, int InputTokens, int OutputTokens)> UpdateMappingsAsync(string currentProposals, string userFeedback)
        {
            string systemPrompt = $@"You are the BricsCAD Semantic Mapping Review Agent.
The system has generated a proposed list of layer mappings natively, but the Human Drafter has intercepted the list and provided English conversational feedback or corrections.

YOUR SOLE JOB is to take the current proposed mappings, apply the Human's conversational corrections, and output the EXACT UPDATED LIST of mappings in the defined JSON format.

CRITICAL RULES (apply in order, stop at the first rule that matches):
0. PURE AGREEMENT — If the user's message is simply agreeing, affirming, or expressing satisfaction with no specific corrections mentioned (e.g. 'looks great', 'that's correct', 'sure go ahead', 'all good', 'perfect', 'that's right', 'sounds good', 'I agree', 'yep', 'fine by me', 'do it'), you MUST return ALL the current proposed mappings UNCHANGED. Do NOT delete anything.
1. SPECIFIC INCLUDE — If the user explicitly lists SPECIFIC mappings to include or keep AND their message implies the others should be dropped, you MUST DELETE all other mappings from the array. DO NOT retain mappings the user omitted.
2. SPECIFIC EXCLUDE — If the user asks to exclude, ignore, or skip specific layers, you MUST physically ERASE those layers from the JSON array.
3. CANCEL — If the user says 'Cancel', 'Stop', or 'End', you should just return an empty array `[]` in the tool calls.

JSON Schema:
{{
  ""tool_calls"": [
    {{
      ""command_name"": ""Semantic Mapping"",
      ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:<SourceLayer>:<TargetLayer>""
    }}
  ]
}}

YOU MUST ONLY OUTPUT VALID JSON MATCHING THIS SCHEMA EXACTLY. DO NOT OUTPUT MARKDOWN, TEXT, OR EXPLANATIONS.
";

            string prompt = $"CURRENT PROPOSED MAPPINGS:\n{currentProposals}\n\nHUMAN CORRECTION / FEEDBACK:\n{userFeedback}\n\nApply the feedback and regenerate the strict JSON list of `NET:LEARN_LAYER_MAPPING` commands.";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);
            return (result.Content, result.TotalTokens, result.InputTokens, result.OutputTokens);
        }

        public async Task<string> ClassifyUserIntentAsync(string userMessage)
        {
            string systemPrompt = @"You are the Mapping Review Classifier for BricsAI.
The system is currently paused, waiting for the user to confirm a list of auto-generated CAD layer mappings before it performs a 'Proofing' action.

Your ONLY job is to read the user's response and classify their intent into exactly ONE of the following five keywords.

[KEYWORDS]
CONFIRM
ABORT
SKIP_AND_PROCEED
ACTION_QUESTION
QUESTION

[DEFINITIONS]
CONFIRM: The user is agreeing to the specific mappings shown to them, providing minor corrections, or telling the system to go ahead with what was suggested. Examples: 'yes', 'looks good', 'that's fine', 'sure', 'go ahead', 'perfect', 'all correct', 'do it', 'proceed with those', 'keep mapping X to Y', 'agreed', NOT: 'no changes needed' — this does not reference or endorse the mapping content, treat as SKIP_AND_PROCEED.
ABORT: The user is outright cancelling the operation, stopping the proofing process entirely, or rejecting the mappings without wanting to continue at all. Examples: 'stop', 'cancel', 'abort', 'nevermind', 'forget it', 'don't do anything', 'revert', 'no don't proceed', 'scrap it'.
SKIP_AND_PROCEED: The user wants to skip or ignore the suggested mappings entirely BUT still wants the proofing action to run without applying any of those mappings. This includes any phrasing that implies 'don't save these, but still proof the drawing'. Examples: 'skip the mappings and proceed', 'ignore those, just proof it', 'don't map anything, just run proofing', 'proceed as-is without mapping', 'just move on', 'forget the suggestions and continue', 'skip this step', 'move forward without mapping', 'proof it without any of those changes', 'don't bother with the mapping, just proof', 'no changes needed', 'no changes needed, proceed', 'no modifications, just proof it', 'leave the mappings, just proceed', 'nothing to change, go ahead'.
ACTION_QUESTION: The user is asking you to perform a specific BricsCAD action — even if phrased as a question. This includes toggling, showing, hiding, isolating, or renaming layers. Examples: 'Can you show only the layers mapped to Expo_Building?', 'Hide everything else', 'Toggle the Expo_Building ones on', 'Can you isolate those layers for me?', 'Show me only the suggested ones'. Do NOT classify as CONFIRM — the user wants a BricsCAD action, not to approve the whole mapping list.
QUESTION: The user is asking for pure information or clarification about the mappings. They are NOT asking the system to do something. Examples: 'What does this map to?', 'How many layers are there?', 'What is Expo_Building used for?', 'Which ones map to Expo_Building?', 'Can you explain this mapping?'.

CRITICAL RULE: If the user phrased something as a question but is clearly asking you to DO something in BricsCAD (show, hide, toggle, isolate, rename layers), classify it as ACTION_QUESTION, not QUESTION.
CRITICAL RULE: SKIP_AND_PROCEED is NOT the same as ABORT. ABORT means the user wants to stop everything. SKIP_AND_PROCEED means they want to skip the mapping step but still proceed with proofing.
CRITICAL RULE: If the user says something like 'no changes needed', 'no modifications', 'nothing to change', or 'leave it as is' — WITHOUT explicitly saying 'looks good', 'correct', 'that's right', or directly referencing the mapping content — classify as SKIP_AND_PROCEED, NOT CONFIRM. 
'No changes needed' means don't apply any mapping changes. CONFIRM requires the user to be endorsing the mapping content itself.

You MUST output strictly one of these five exact words. Do not output any other text.";

            string prompt = $"USER MESSAGE: {userMessage}";
            
            var result = await CallModelAsync(systemPrompt, prompt, expectJson: false);
            BricsAI.Core.LoggerService.LogAgentPrompt("MappingClassifier", result.Content);
            return result.Content.Trim().ToUpper();
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
