# BricsAI Developer Onboarding

This guide gets a developer productive on the BricsAI stack, which has two runtime entrypoints sharing common infrastructure.

## 1) Architecture Snapshot

Two data paths, one plugin/COM layer:

- Claude client -> MCP stdio -> BricsAI.McpServer -> STA COM bridge -> plugin execution (V15/V19) -> BricsCAD active document
- BricsAI.Overlay (WPF) -> multi-agent pipeline (Surveyor -> Mapper -> MappingReview -> Executor -> Validator) -> ComClient (its own STA UI thread) -> plugin execution (V15/V19) -> BricsCAD active document

Core projects:

- BricsAI.McpServer: host, tools, prompts, COM orchestration via a dedicated STA thread (needed because it's a console/stdio host with no natural UI thread)
- BricsAI.Overlay: WPF chat app; multi-agent LLM pipeline calling the Anthropic API directly; COM calls run on the UI thread, which is already STA
- BricsAI.Core: `IToolPlugin` contract, `KnowledgeService` (SQLite), `LoggerService`, `Mock` (shared in-memory mock CAD backend)
- BricsAI.Plugins.V15Tools: V15 command implementations
- BricsAI.Plugins.V19Tools: V19 command implementations

Both McpServer and Overlay load **both** plugin assemblies at startup (`PluginManager.LoadPlugins()` scans for every `BricsAI.Plugins*.dll` next to the exe) and pick the plugin whose `TargetVersion` matches the connected BricsCAD version.

## 2) Environment Requirements

- Windows
- BricsCAD V15 or V19 installed (or use the mock backend — see below)
- .NET 9 SDK
- Claude Code or Claude Desktop (for McpServer) — or an Anthropic API key (for Overlay)

## 3) Build and Run

Build solution:

```powershell
dotnet build BricsAI.sln -c Release
```

Run MCP server:

```powershell
dotnet run --project .\BricsAI.McpServer\BricsAI.McpServer.csproj -c Release
```

Run the Overlay app (set `Anthropic.ApiKey` in `BricsAI.Overlay\appsettings.json` first — gitignored, never commit a real key):

```powershell
dotnet run --project .\BricsAI.Overlay\BricsAI.Overlay.csproj -c Release
```

Register McpServer in Claude Code (local scope):

```powershell
claude mcp add --scope local --transport stdio bricsai -- "C:\Users\gbhowmik\Documents\BricsAI_Claude_v3.3.0\BricsAI.McpServer\bin\Release\net9.0-windows\BricsAI.McpServer.exe"
```

### Develop without a running BricsCAD instance

Set `BRICSAI_MOCK_CAD=1` before launching either app to connect to an in-memory mock (`BricsAI.Core.Mock.MockDrawingSeeder`) instead of real COM — seeded with standard A2Z layers, a few vendor layers, and 5 booth outlines (one intentionally missing its number, to exercise `count_empty_booths`/`select_empty_booths`). Real Anthropic API calls still happen in Overlay under mock mode; only the CAD layer is faked.

## 4) Key Runtime Services

- StaComHost (McpServer only): dedicated STA thread/message pump for COM safety, since a stdio host has no natural UI thread
- ComClient: BricsCAD access and command dispatch, plus active CAD file metadata. Each app has its own copy (`BricsAI.McpServer/Services/ComClient.cs`, `BricsAI.Overlay/Services/ComClient.cs`) — near-identical logic, kept separate rather than shared so each app's execution guards (e.g. Overlay's proofing-completeness auto-guards) stay independent.
- PluginManager: version-aware plugin loading (also duplicated per app, same reason)
- ToolExec (McpServer only): common execution wrapper with per-action usage logging

## 5) Knowledge Store Behavior

Persistent mapping/rule memory is a **local SQLite database, shared between both apps**:

- Runtime location: `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` (override with `BRICSAI_KNOWLEDGE_DIR`)
- Checked-in starter copy: `agent_knowledge.db` at the **solution root** — never read/written by either app at runtime; copy it to `%LOCALAPPDATA%\BricsAI\` manually when setting up a new machine to seed it with the accumulated mapping history
- Schema: `layer_mappings` (`source_layer` TEXT PRIMARY KEY, upserted — no dedupe pass needed) and `free_form_rules` (append-only)
- `KnowledgeService.GetLearnings()` — full text dump (mappings + rules), used by `get_learned_mappings` / any caller that wants everything
- `KnowledgeService.GetFreeFormRules()` — rules only, no mappings; use this when embedding knowledge into an LLM prompt, since dumping hundreds of mapping lines into every call burns tokens for no benefit (mappings are applied deterministically via `apply_layer_mappings`, not by the model reasoning about them)
- `KnowledgeService.GetLayerMappingsDictionary()` — indexed dictionary read
- `KnowledgeService.CompactFile()` — now runs `VACUUM` (kept the old name since it's the one existing caller, `BricsAI.McpServer/Program.cs`); no longer a dedupe pass since upserts already prevent duplicate rows

## 6) MCP Tool / Overlay Command Parity

- get_layer_geometry(layer, offset, maxEntities) / `NET:GET_LAYER_GEOMETRY`
- export_layer_snapshot(layer, format) / `NET:EXPORT_LAYER_SNAPSHOT`
- select_layer, select_outer/inner, select_building_lines, unlock_layers_by_prefix — implemented in the shared plugins and always callable via MCP; Overlay's `ExecutorAgent` only recently gained visibility into these (they were missing from the plugins' `GetPromptExample()`, which is the only thing Overlay's Executor prompt reads to know what `NET:` commands exist — MCP tool descriptions are separate `[McpServerTool(Description=...)]` attributes and were unaffected)

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

- a running BricsAI.McpServer or BricsAI.Overlay process holds the executable or plugin DLLs

Fix:

```powershell
Get-Process BricsAI.McpServer,BricsAI.Overlay -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln -c Release
```

## 9) Safe Contribution Workflow

1. Build before and after change.
2. Validate tool behavior with a live drawing, or with `BRICSAI_MOCK_CAD=1` for a fast, deterministic dev loop.
3. Keep destructive actions approval-gated in prompts/workflows.
4. If you change something in `BricsAI.Plugins.V15Tools`/`V19Tools` or `BricsAI.Core`, remember both McpServer and Overlay consume it — check both, not just the one you're working in.
5. Update docs when tools or flow change.
6. Commit with clear scope and operational notes.

## 10) Suggested First Validation Script

Against a real BricsCAD instance, or headlessly with `BRICSAI_MOCK_CAD=1`:

1. list_layers
2. get_unmapped_layers
3. poll_layer_semantics on unknown layers
4. get_layer_geometry for ambiguous cases
5. apply_layer_mappings on approved set
6. run_full_proofing (McpServer) / send "proof this drawing" to Overlay, which emits the equivalent `NET:RUN_FULL_PROOFING`
7. export_layer_snapshot for QA artifacts

For Overlay specifically, also exercise the tabular mapping review: trigger a proofing run against a drawing with unmapped layers, then reply with `include 1,3`, `exclude 2`, a mid-review `remember X always maps to Y`, and finally `confirm all`.
