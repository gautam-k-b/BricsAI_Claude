using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BricsAI.Overlay.Services;

namespace BricsAI.Overlay.Services.Agents
{
    public abstract class BaseAgent
    {
        private readonly AnthropicRuntime.ProviderConfiguration _providerConfiguration;
        
        public string Name { get; protected set; } = "BaseAgent";

        public BaseAgent()
        {
            _providerConfiguration = AnthropicRuntime.LoadConfiguration();
        }

        protected async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> CallModelAsync(string systemPrompt, string userPrompt, bool expectJson = false)
        {
            if (!AnthropicRuntime.IsApiKeyConfigured(_providerConfiguration.ApiKey))
            {
                return (expectJson
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Please configure your Anthropic API Key."", ""lisp_code"": """" }}] }}"
                    : "Error: Please configure your Anthropic API Key.", 0, 0, 0);
            }

            try
            {
                var result = await AnthropicRuntime.SendMessageAsync(_providerConfiguration, systemPrompt, userPrompt);
                var content = expectJson ? AnthropicRuntime.StripJsonFences(result.Content) : result.Content;

                return (content.Trim(), result.TotalTokens, result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                string fullError = ex.ToString().Replace("\"", "'").Replace("\\", "/");
                string safeMsg = ex.Message.Replace("\"", "'").Replace("\\", "/");
                System.IO.File.WriteAllText("AI_Error.txt", fullError);
                return (expectJson
                    ? $@"{{ ""error"": true, ""tool_calls"": [], ""message"": ""{safeMsg}"" }}"
                    : $"Agent {Name} Error: {safeMsg}", 0, 0, 0);
            }
        }

        protected async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> CallModelWithImagesAsync(
            string systemPrompt, string userPrompt, IReadOnlyList<string> imagePaths, bool expectJson = false)
        {
            if (!AnthropicRuntime.IsApiKeyConfigured(_providerConfiguration.ApiKey))
            {
                return (expectJson
                    ? $@"{{ ""tool_calls"": [{{ ""command_name"": ""NET:MESSAGE: Please configure your Anthropic API Key."", ""lisp_code"": """" }}] }}"
                    : "Error: Please configure your Anthropic API Key.", 0, 0, 0);
            }

            try
            {
                var result = await AnthropicRuntime.SendMessageWithImagesAsync(_providerConfiguration, systemPrompt, userPrompt, imagePaths);
                var content = expectJson ? AnthropicRuntime.StripJsonFences(result.Content) : result.Content;
                return (content.Trim(), result.TotalTokens, result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                string fullError = ex.ToString().Replace("\"", "'").Replace("\\", "/");
                string safeMsg = ex.Message.Replace("\"", "'").Replace("\\", "/");
                System.IO.File.WriteAllText("AI_Error.txt", fullError);
                return (expectJson
                    ? $@"{{ ""error"": true, ""tool_calls"": [], ""message"": ""{safeMsg}"" }}"
                    : $"Agent {Name} Error: {safeMsg}", 0, 0, 0);
            }
        }
    }
}
