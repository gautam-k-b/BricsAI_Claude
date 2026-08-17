using System;
using System.Threading.Tasks;
using BricsAI.Core;
using System.Linq;

namespace BricsAI.Overlay.Services
{
    public class LLMService
    {
        private readonly AnthropicRuntime.ProviderConfiguration _providerConfiguration;

        private readonly PluginManager _pluginManager;

        public LLMService()
        {
            _pluginManager = new PluginManager();
            _pluginManager.LoadPlugins();
            _providerConfiguration = AnthropicRuntime.LoadConfiguration();
        }

        public async Task<string> GenerateScriptAsync(string userPrompt, int majorVersion, string currentLayers = "")
        {
            if (!AnthropicRuntime.IsApiKeyConfigured(_providerConfiguration.ApiKey))
            {
                return $"(alert \"Please configure your Anthropic API Key in appsettings.json.\")";
            }

            var applicablePlugins = _pluginManager.GetPluginsForVersion(majorVersion).ToList();
            
            var toolsPrompt = string.Join("\n\n", applicablePlugins.Select(p => p.GetPromptExample()));

            var layersContext = string.IsNullOrWhiteSpace(currentLayers) ? "" : $"\nCURRENT DRAWING LAYERS:\n{currentLayers}\nUse these existing layer names when migrating unknown geometry to destination standard layers.\n";

            var systemPrompt = $@"You are an expert BricsCAD automation agent. Your goal is to control BricsCAD V{majorVersion} by outputting structured JSON commands.
                                {layersContext}
                                
                                [USER PREFERENCES & LEARNED RULES]
                                {BricsAI.Core.KnowledgeService.GetLearnings()}

                                YOU MUST OUTPUT ONLY VALID JSON. NO MARKDOWN. NO EXPLANATIONS.

                                CRITICAL RULES:
                                1. NEVER invent custom LISP selection loops (NO sssetfirst, NO vla-getboundingbox). 
                                2. If the user asks to select objects by layer, or specifically inner/outer objects, YOU MUST use the exact `NET:` prefix commands shown in the tools below. The C# host handles the geometry natively.
                                3. ALWAYS prioritize using the provided tool examples. DO NOT hallucinate commands like `_UNSELECT` or nested LISP evaluations for selections.
                                4. MACRO SEQUENCES: You are allowed and encouraged to output massive JSON arrays containing 10+ `tool_calls` to sequentially orchestrate full workflows (e.g., if asked to 'proof' a file).
                                5. PROOFING ORDER OF OPERATIONS: If asked to proof a drawing, you MUST execute exactly this sequence:
                                   A. Explode & Flatten: Run EXPLODE 3-4 times.
                                   B. Layer Standardization: Run the A2ZLAYERS command to create all standard destination layers.
                                   C. Filter Noise: Delete all layers containing 'dim', 'delete', or 'frozen' in their name.
                                   D. Geometric Migration: Use NET: Geometric Classifiers (like NET:SELECT_BOOTH_BOXES) to identify logical elements and move them to standard layers (Expo_BoothOutline, Expo_Building, Expo_Columns).
                                   E. Final Visual Verification: Run A2ZCOLOR command as the VERY LAST step.

                                JSON Schema:
                                {{
                                  ""tool_calls"": [
                                    {{
                                      ""command_name"": ""The primary CAD command or logical name (e.g., 'EXPLODE', 'NET_SELECT_OUTER')"",
                                      ""lisp_code"": ""The actual string to send. (e.g. '(command \""_.CIRCLE\"" ...)' or 'NET:SELECT_OUTER: outlines' or 'NET:MESSAGE: Hello')""
                                    }}
                                  ]
                                }}

                                Basic Example:
                                User: 'Draw a circle at 0,0 with radius 10'
                                Response: {{ ""tool_calls"": [{{ ""command_name"": ""CIRCLE"", ""lisp_code"": ""(command \""_.CIRCLE\"" \""0,0\"" \""10\"")"" }}] }}

                                {toolsPrompt}
                                ";

            try
            {
                var result = await AnthropicRuntime.SendMessageAsync(_providerConfiguration, systemPrompt, userPrompt);
                var script = AnthropicRuntime.StripJsonFences(result.Content);
                
                if (script == null) return string.Empty;

                return script.Trim();
            }
            catch (Exception ex)
            {
                // Fallback valid JSON for error
                return $@"{{ ""tool_calls"": [{{ ""command_name"": ""ALERT"", ""lisp_code"": ""(alert \""LLM Error: {ex.Message}\"")"" }}] }}";
            }
        }
    }
}
