using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BricsAI.Overlay.Services;

namespace BricsAI.Overlay.Services.Agents
{
    /// <summary>
    /// Raised when the LLM cannot be reached or rejects the request (bad key, network, quota...).
    /// Callers must stop the workflow — proceeding without the LLM produces garbage results.
    /// </summary>
    public sealed class LlmUnavailableException : Exception
    {
        public LlmUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

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
                throw new LlmUnavailableException(AnthropicRuntime.NoApiKeyMessage);

            try
            {
                var result = await AnthropicRuntime.SendMessageAsync(_providerConfiguration, systemPrompt, userPrompt);
                var content = expectJson ? AnthropicRuntime.StripJsonFences(result.Content) : result.Content;

                return (content.Trim(), result.TotalTokens, result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText("AI_Error.txt", ex.ToString()); } catch { }
                throw new LlmUnavailableException(AnthropicRuntime.DescribeFailure(ex), ex);
            }
        }

        protected async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> CallModelWithImagesAsync(
            string systemPrompt, string userPrompt, IReadOnlyList<string> imagePaths, bool expectJson = false)
        {
            if (!AnthropicRuntime.IsApiKeyConfigured(_providerConfiguration.ApiKey))
                throw new LlmUnavailableException(AnthropicRuntime.NoApiKeyMessage);

            try
            {
                var result = await AnthropicRuntime.SendMessageWithImagesAsync(_providerConfiguration, systemPrompt, userPrompt, imagePaths);
                var content = expectJson ? AnthropicRuntime.StripJsonFences(result.Content) : result.Content;
                return (content.Trim(), result.TotalTokens, result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText("AI_Error.txt", ex.ToString()); } catch { }
                throw new LlmUnavailableException(AnthropicRuntime.DescribeFailure(ex), ex);
            }
        }
    }
}
