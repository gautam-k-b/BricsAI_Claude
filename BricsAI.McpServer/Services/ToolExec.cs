using System.Threading.Tasks;

namespace BricsAI.McpServer.Services
{
    /// <summary>
    /// Shared helper so every [McpServerTool] method funnels its COM work through the
    /// STA pump the same way, instead of re-writing the InvokeAsync/RunNetCommand pair everywhere.
    /// </summary>
    internal static class ToolExec
    {
        public static Task<string> RunAsync(ComClient comClient, StaComHost sta, string netCmd)
            => sta.InvokeAsync(() => comClient.RunNetCommand(netCmd));

        public static Task<string> RawAsync(ComClient comClient, StaComHost sta, string command)
            => sta.InvokeAsync(() => comClient.SendRawCommand(command));
    }
}
