using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BricsAI.Core;

namespace BricsAI.McpServer.Services
{
    /// <summary>
    /// Shared helper so every [McpServerTool] method funnels its COM work through the
    /// STA pump the same way, instead of re-writing the InvokeAsync/RunNetCommand pair everywhere.
    /// </summary>
    internal static class ToolExec
    {
        public static Task<string> RunAsync(ComClient comClient, StaComHost sta, string netCmd)
            => ExecuteWithUsageLoggingAsync(comClient, sta, "NET", netCmd, () => comClient.RunNetCommand(netCmd));

        public static Task<string> RawAsync(ComClient comClient, StaComHost sta, string command)
            => ExecuteWithUsageLoggingAsync(comClient, sta, "RAW", command, () => comClient.SendRawCommand(command));

        private static Task<string> ExecuteWithUsageLoggingAsync(
            ComClient comClient,
            StaComHost sta,
            string actionType,
            string action,
            Func<string> execute)
            => sta.InvokeAsync(() =>
            {
                var sw = Stopwatch.StartNew();
                string cadFile = comClient.GetActiveDocumentNameOrPath();
                string output = execute();
                sw.Stop();

                int inputChars = action?.Length ?? 0;
                int outputChars = output?.Length ?? 0;
                int inputTokens = EstimateTokens(action);
                int outputTokens = EstimateTokens(output);

                LoggerService.LogMcpUsage(
                    actionType: actionType,
                    action: action,
                    cadFile: cadFile,
                    inputChars: inputChars,
                    outputChars: outputChars,
                    estimatedInputTokens: inputTokens,
                    estimatedOutputTokens: outputTokens,
                    elapsedMs: sw.ElapsedMilliseconds);

                return output;
            });

        private static int EstimateTokens(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Conservative heuristic for GPT-family tokenization: ~4 chars/token for mixed ASCII text.
            return (int)Math.Ceiling(text.Length / 4.0);
        }
    }
}
