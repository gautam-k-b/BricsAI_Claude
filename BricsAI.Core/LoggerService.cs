using System;
using System.IO;

namespace BricsAI.Core
{
    public static class LoggerService
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "transaction_log.txt");
        private static readonly object _lock = new object();

        /// <summary>
        /// Appends a new transaction event to the rolling log.
        /// </summary>
        /// <param name="source">The origin of the log (e.g., 'USER', 'EXECUTOR', 'BRICSCAD')</param>
        /// <param name="message">The content to log</param>
        public static void LogTransaction(string source, string message)
        {
            try
            {
                lock (_lock)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logEntry = $"[{timestamp}] [{source}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, logEntry);
                }
            }
            catch
            {
                // Silently fail if logging is locked by another process or permission denied
            }
        }

        public static void LogUserMessage(string message) => LogTransaction("USER", message);
        public static void LogAgentPrompt(string agentName, string generatedPlan) => LogTransaction($"AGENT:{agentName.ToUpper()}", $"Generated Plan:\n{generatedPlan}");
        public static void LogComExecution(string commandName, string lispCode) => LogTransaction("BRICSCAD:SEND", $"Tool: [{commandName}] -> {lispCode}");
        public static void LogComResponse(string response) => LogTransaction("BRICSCAD:RECEIVE", response);
    }
}
