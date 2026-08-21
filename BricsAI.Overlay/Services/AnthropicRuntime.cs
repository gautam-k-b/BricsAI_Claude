using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;

namespace BricsAI.Overlay.Services
{
    internal static class AnthropicRuntime
    {
        internal const string DefaultModel = "claude-sonnet-4-5-20250929";

        internal sealed class ProviderConfiguration
        {
            public string? ApiKey { get; init; }
            public string Model { get; init; } = DefaultModel;
            public string? ApiUrl { get; init; }
        }

        private sealed class AppSettings
        {
            public AnthropicSettings? Anthropic { get; set; }
        }

        private sealed class AnthropicSettings
        {
            public string? ApiKey { get; set; }
            public string? Model { get; set; }
            public string? ApiUrl { get; set; }
        }

        internal static ProviderConfiguration LoadConfiguration()
        {
            try
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                var settingsPath = Path.Combine(basePath, "appsettings.json");

                if (!File.Exists(settingsPath))
                {
                    return new ProviderConfiguration();
                }

                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                var configuredModel = settings?.Anthropic?.Model;

                return new ProviderConfiguration
                {
                    ApiKey = settings?.Anthropic?.ApiKey,
                    Model = string.IsNullOrWhiteSpace(configuredModel) ? DefaultModel : configuredModel!,
                    ApiUrl = string.IsNullOrWhiteSpace(settings?.Anthropic?.ApiUrl) ? null : settings!.Anthropic!.ApiUrl
                };
            }
            catch
            {
                return new ProviderConfiguration();
            }
        }

        internal static bool IsApiKeyConfigured(string? apiKey)
        {
            return !string.IsNullOrWhiteSpace(apiKey)
                && !string.Equals(apiKey, "YOUR_ANTHROPIC_API_KEY_HERE", StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> SendMessageAsync(
            ProviderConfiguration configuration,
            string systemPrompt,
            string userPrompt)
        {
            var client = string.IsNullOrWhiteSpace(configuration.ApiUrl)
                ? new AnthropicClient { ApiKey = configuration.ApiKey }
                : new AnthropicClient { ApiKey = configuration.ApiKey, BaseUrl = configuration.ApiUrl };

            var canApplySystemPrompt = CanApplySystemPrompt(systemPrompt);
            var finalUserPrompt = canApplySystemPrompt
                ? userPrompt
                : $"SYSTEM INSTRUCTIONS:\n{systemPrompt}\n\nUSER REQUEST:\n{userPrompt}";

            var parameters = new MessageCreateParams
            {
                MaxTokens = 8192,
                Model = string.IsNullOrWhiteSpace(configuration.Model) ? DefaultModel : configuration.Model,
                System = canApplySystemPrompt ? systemPrompt : null,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = finalUserPrompt
                    }
                ]
            };

            var message = await client.Messages.Create(parameters);
            var textBlocks = new System.Collections.Generic.List<string>();
            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var textBlock))
                    textBlocks.Add(textBlock.Text);
            }
            var content = string.Join("\n", textBlocks).Trim();

            var inputTokens = (int)message.Usage.InputTokens;
            var outputTokens = (int)message.Usage.OutputTokens;
            var totalTokens = inputTokens + outputTokens;

            return (content, totalTokens, inputTokens, outputTokens);
        }

        // Returns null for unsupported formats (e.g. BMP) so callers can skip those images.
        internal static MediaType? GetImageMediaType(string filePath)
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".png"          => MediaType.ImagePng,
                ".jpg" or ".jpeg" => MediaType.ImageJpeg,
                ".gif"          => MediaType.ImageGif,
                ".webp"         => MediaType.ImageWebP,
                _               => null
            };
        }

        internal static async Task<(string Content, int TotalTokens, int InputTokens, int OutputTokens)> SendMessageWithImagesAsync(
            ProviderConfiguration configuration,
            string systemPrompt,
            string userPrompt,
            IReadOnlyList<string> imagePaths)
        {
            var client = string.IsNullOrWhiteSpace(configuration.ApiUrl)
                ? new AnthropicClient { ApiKey = configuration.ApiKey }
                : new AnthropicClient { ApiKey = configuration.ApiKey, BaseUrl = configuration.ApiUrl };

            var contentBlocks = new List<ContentBlockParam>();

            foreach (var path in imagePaths)
            {
                if (!File.Exists(path)) continue;
                var mediaType = GetImageMediaType(path);
                if (mediaType == null) continue;
                var bytes = await File.ReadAllBytesAsync(path);
                var base64 = Convert.ToBase64String(bytes);
                contentBlocks.Add(new ContentBlockParam(new ImageBlockParam
                {
                    Source = new ImageBlockParamSource(new Base64ImageSource
                    {
                        MediaType = mediaType.Value,
                        Data = base64
                    })
                }));
            }

            var canApplySystemPrompt = CanApplySystemPrompt(systemPrompt);
            var finalUserPrompt = canApplySystemPrompt
                ? userPrompt
                : $"SYSTEM INSTRUCTIONS:\n{systemPrompt}\n\nUSER REQUEST:\n{userPrompt}";

            contentBlocks.Add(new ContentBlockParam(new TextBlockParam { Text = finalUserPrompt }));

            var parameters = new MessageCreateParams
            {
                MaxTokens = 8192,
                Model = string.IsNullOrWhiteSpace(configuration.Model) ? DefaultModel : configuration.Model,
                System = canApplySystemPrompt ? systemPrompt : null,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = contentBlocks
                    }
                ]
            };

            var message = await client.Messages.Create(parameters);
            var textBlocks = new System.Collections.Generic.List<string>();
            foreach (var block in message.Content)
            {
                if (block.TryPickText(out var textBlock))
                    textBlocks.Add(textBlock.Text);
            }
            var content = string.Join("\n", textBlocks).Trim();

            var inputTokens = (int)message.Usage.InputTokens;
            var outputTokens = (int)message.Usage.OutputTokens;
            var totalTokens = inputTokens + outputTokens;

            return (content, totalTokens, inputTokens, outputTokens);
        }

        internal static string StripJsonFences(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var cleaned = content.Trim();
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase) || cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return cleaned.Trim();
        }

        private static bool CanApplySystemPrompt(string systemPrompt)
        {
            return !string.IsNullOrWhiteSpace(systemPrompt);
        }
    }
}
