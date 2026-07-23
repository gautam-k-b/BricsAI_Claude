using BricsAI.Core;
using BricsAI.McpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Remove duplicate entries accumulated from prior sessions before the server
// starts accepting tool calls. Safe to run every startup — no-op if the file
// is already clean.
KnowledgeService.CompactFile();

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for the MCP JSON-RPC stream — all logging must go to stderr.
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<StaComHost>();
builder.Services.AddSingleton<ComClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly();

var host = builder.Build();

// Force the STA COM pump thread to start and become ready *before* the server
// starts accepting tool calls over stdio, so no early call can race pump startup.
host.Services.GetRequiredService<StaComHost>();

await host.RunAsync();
