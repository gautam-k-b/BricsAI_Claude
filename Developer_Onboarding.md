# BricsAI Developer Onboarding

This guide gets a developer productive on the MCP-first BricsAI stack.

## 1) Architecture Snapshot

Data path:

Claude client -> MCP stdio -> BricsAI.McpServer -> STA COM bridge -> plugin execution (V15/V19) -> BricsCAD active document

Core projects:

- BricsAI.McpServer: host, tools, prompts, COM orchestration
- BricsAI.Core: interfaces, knowledge service, logging
- BricsAI.Plugins.V15Tools: V15 command implementations
- BricsAI.Plugins.V19Tools: V19 command implementations

Removed project:

- BricsAI.Overlay is intentionally retired from active architecture.

## 2) Environment Requirements

- Windows
- BricsCAD V15 or V19 installed
- .NET 9 SDK
- Claude Code or Claude Desktop

## 3) Build and Run

Build solution:

```powershell
dotnet build BricsAI.sln -c Release
```

Run MCP server:

```powershell
dotnet run --project .\BricsAI.McpServer\BricsAI.McpServer.csproj -c Release
```

Register in Claude Code (local scope):

```powershell
claude mcp add --scope local --transport stdio bricsai -- "C:\Users\gbhowmik\Documents\BricsAI_Claude_v3.3.0\BricsAI.McpServer\bin\Release\net9.0-windows\BricsAI.McpServer.exe"
```

## 4) Key Runtime Services

- StaComHost: dedicated STA thread/message pump for COM safety
- ComClient: BricsCAD access and command dispatch, plus active CAD file metadata
- PluginManager: version-aware plugin loading
- ToolExec: common execution wrapper with per-action usage logging

## 5) Knowledge Store Behavior

Knowledge file:

- agent_knowledge.txt near executing server binary

Current guarantees:

- startup compaction to reduce duplicates
- dedupe on write (latest source mapping wins)
- dedupe on read for cleaner prompt context
- parse caching by last write time to reduce overhead

## 6) New MCP Features to Know

- get_layer_geometry(layer, offset, maxEntities)
- export_layer_snapshot(layer, format)

Design intent:

- geometry paging protects context/window size on large layers
- snapshot export supports human visual verification

## 7) Logging and Observability

transaction_log.txt now includes:

- action category and text
- CAD file name/path
- input/output character counts
- estimated token counts
- elapsed milliseconds

Interpretation rule:

- treat token fields as directional usage signals, not billing truth

## 8) Common Failure Mode: Build Locks

Cause:

- running BricsAI.McpServer process holds executable or plugin DLLs

Fix:

```powershell
Get-Process BricsAI.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln -c Release
```

## 9) Safe Contribution Workflow

1. Build before and after change.
2. Validate tool behavior with a live drawing.
3. Keep destructive actions approval-gated in prompts/workflows.
4. Update docs when tools or flow change.
5. Commit with clear scope and operational notes.

## 10) Suggested First Validation Script

1. list_layers
2. get_unmapped_layers
3. poll_layer_semantics on unknown layers
4. get_layer_geometry for ambiguous cases
5. apply_layer_mappings on approved set
6. run_full_proofing
7. export_layer_snapshot for QA artifacts
