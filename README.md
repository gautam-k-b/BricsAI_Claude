# BricsAI Automation Stack for BricsCAD

BricsAI is a local automation stack for BricsCAD proofing workflows, consumable two ways:

- **BricsAI.McpServer** — an MCP server consumed from Claude Code or Claude Desktop over stdio, using your Claude subscription instead of a metered API key.
- **BricsAI.Overlay** — a standalone WPF desktop app with its own 4-agent pipeline (Surveyor, Mapper, Executor, Validator) that calls the Anthropic API directly with your own key.

Both share the same BricsCAD COM automation layer (version-specific plugin DLLs), the same local SQLite knowledge store, and the same in-memory mock CAD backend for development without a running BricsCAD instance.

## Quick Documents

- End-user Overlay guide: Developer_Onboarding.md
- Detailed training playbook: Training_Guide.md
- Release history: RELEASE_NOTES.md

## Current Architecture

```text
Claude Code / Claude Desktop          BricsAI.Overlay (WPF)
          |                                    |
          | MCP stdio                          | Anthropic API (direct)
          v                                    v
BricsAI.McpServer                     Surveyor -> Mapper -> MappingReview -> Executor -> Validator
(Host + Tool Surface + Prompt Surface)         |
          |                                    |
          | STA COM bridge                     | (WPF UI thread is already STA)
          v                                    v
                    ComClient -> PluginManager
                              |
                              v
        BricsAI.Plugins.V15Tools / BricsAI.Plugins.V19Tools
                              |
                              v
                  BricsCAD ActiveDocument (V15/V19)
                  -- or -- BricsAI.Core.Mock (BRICSAI_MOCK_CAD=1)
```

### Key Runtime Components

- BricsAI.McpServer
  - Hosts MCP over stdio
  - Exposes tools and prompts from assembly
  - Forces COM execution through a dedicated STA thread
- BricsAI.Overlay
  - WPF chat UI driving the same COM/plugin layer directly (no MCP hop)
  - Multi-agent pipeline: SurveyorAgent, MapperAgent, MappingReviewAgent, ExecutorAgent, ValidatorAgent
  - Tabular mapping review always shown before proofing — even when all layers are already DB-known — so no proofing ever starts without human approval
  - Phase 1 name classification batches at 40 layers per LLM call to avoid token-limit failures on large drawings
  - INCLUDE auto-excludes all other rows; EXCLUDE auto-includes all other rows; INCLUDE_EXCLUDE for explicit combined decisions; HIGH_CONFIDENCE_ONLY for one-shot bulk decision by confidence level
  - Mid-review commands (MEMORIZE, ABORT, QUESTION, ACTION) work at any time without interrupting the table state
  - Buttons and input field lock during active processing; unlock automatically when the table review is waiting for input
- BricsAI.Core
  - IToolPlugin contract
  - KnowledgeService — SQLite-backed persistent mappings and rules (see Knowledge Store Behavior below)
  - LoggerService for transaction and MCP usage logs
  - Mock — in-memory mock BricsCAD/AutoCAD COM surface, shared by both apps via `BRICSAI_MOCK_CAD=1`
- BricsAI.Plugins.V15Tools
  - BricsCAD V15 command implementations
- BricsAI.Plugins.V19Tools
  - BricsCAD V19 command implementations

## Main MCP Tool Groups

### Layer tools

- list_layers (excludes frozen layers — see Frozen Layer Handling below)
- apply_layer_mappings
- rename_unmapped_layers_to_deleted
- delete_layers_by_prefix
- erase_entities_on_layer
- lock_booth_layers
- unlock_layers_by_prefix
- poll_layer_semantics
- get_layer_geometry
- export_layer_snapshot
- learn_layer_mapping
- isolate_layers / show_layer / hide_layer / show_all_layers

### Geometry tools

- prepare_geometry
- select_booth_boxes
- select_empty_booths
- count_empty_booths
- select_building_lines
- select_columns
- select_utilities
- explode_entities_by_type
- delete_non_standard_entities

**Geometry preparation behaviour:**
- POINT entities are deleted upfront before the explosion loop — they cannot be exploded and carry no useful geometry.
- All other non-standard entity types (HATCH, unknown block types, etc.) are attempted for explosion each pass.
- `explode_entities_by_type` counts entities before and after each attempt; if the count does not decrease, the type is skipped immediately instead of exhausting all retry passes.
- After all passes, any entities that still could not be exploded are erased and a `WARNING:` note is included in the result. ValidatorAgent treats this as a partial failure and surfaces it to the user.
- `prepare_geometry` returns `"All complex entities were successfully exploded."` on a clean run, or `"WARNING: N entities could not be exploded after N passes and were erased..."` when erasure occurred.

### Proofing tools

- run_full_proofing (BricsAI.Overlay's Executor agent triggers the same deterministic sequence via the `NET:RUN_FULL_PROOFING` pseudo-command instead of hand-assembling every step in its prompt)
- clean_deleted_layers

### Memory tools

- get_learned_mappings
- get_unmapped_layers
- learn_rule

## New Geometry and Snapshot Capabilities

### get_layer_geometry

Returns entity-level JSON for a specific layer and supports pagination:

- offset: zero-based start index
- maxEntities: page size (1-1000)
- response includes TotalCount, ReturnedCount, Truncated, NextOffset

Use this when poll_layer_semantics is not enough for ambiguous mapping decisions.

### export_layer_snapshot

Exports a visual snapshot for a layer into a temp folder and returns file metadata including the path.

- Request format supports common values such as BMP, PNG, JPG
- If requested format is unavailable in host CAD export path, the server falls back to BMP

## Knowledge Store Behavior

Persistent mapping/rule memory is stored in a local SQLite database, **shared between BricsAI.McpServer and BricsAI.Overlay** — a mapping learned in one app is immediately visible in the other.

- Default location: `%LOCALAPPDATA%\BricsAI\agent_knowledge.db`
- Override: set `BRICSAI_KNOWLEDGE_DIR` to point either app at a different directory (mainly used for test/mock runs)
- Two tables: `layer_mappings` (upserted by `source_layer`, indexed — no full-file scan needed) and `free_form_rules` (free-form behavioral rules like "always run X before Y")
- `agent_knowledge.db` at the **solution root** is a checked-in starter/seed copy — it is never read or written by either app at runtime. On a new machine, manually copy it to `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` before first run to seed a fresh install with the accumulated mapping history instead of starting empty.
- `KnowledgeService.GetFreeFormRules()` returns only the free-form rules (not the full mapping history) for callers that just need behavioral guidance without paying the token cost of dumping every learned mapping into a prompt — layer mappings are applied deterministically via `apply_layer_mappings` / `NET:APPLY_LAYER_MAPPINGS` and don't need to be restated to the model.

## Usage Logging and Token Estimates

Each MCP action now logs usage metadata with timestamp:

- action type (NET or RAW)
- command/action text
- active CAD file name/path
- input and output character counts
- estimated input and output tokens
- elapsed milliseconds

Important: token values are estimates, not provider billing tokens.

Log file location:

- BricsAI.McpServer/bin/<Configuration>/net9.0-windows/transaction_log.txt

Usage entries are written with source MCP:USAGE.

## Setup and Run

### Prerequisites

- BricsCAD V15 or V19 running locally
- .NET 9 SDK
- Claude Code or Claude Desktop

### Build

```powershell
dotnet build BricsAI.sln -c Release
```

### Run MCP server manually

```powershell
dotnet run --project .\BricsAI.McpServer\BricsAI.McpServer.csproj
```

### Add to Claude Code (local scope in current project)

```powershell
claude mcp add --scope local --transport stdio bricsai -- "C:\Users\gbhowmik\Documents\BricsAI_Claude_v3.3.0\BricsAI.McpServer\bin\Release\net9.0-windows\BricsAI.McpServer.exe"
```

Check status:

```powershell
claude mcp get bricsai
```

### Run BricsAI.Overlay instead

Set your Anthropic API key in `BricsAI.Overlay\appsettings.json` (`Anthropic.ApiKey` — gitignored, never commit a real key), then run:

```powershell
dotnet run --project .\BricsAI.Overlay\BricsAI.Overlay.csproj
```

### Develop/test without a running BricsCAD instance

Both apps support an in-memory mock CAD backend seeded with a representative drawing (standard A2Z layers, a few vendor layers, booth outlines with one intentionally missing a number). Set the environment variable before launch:

```powershell
$env:BRICSAI_MOCK_CAD = "1"
dotnet run --project .\BricsAI.Overlay\BricsAI.Overlay.csproj
```

Real API calls still happen (mock mode only replaces the BricsCAD COM layer), so agent responses and token usage are real.

## Recommended End User Flow

1. Run read-only preflight summary
2. Auto-classify unmapped layers
3. Escalate ambiguous layers with get_layer_geometry paging
4. Apply approved/high-confidence mappings
5. Run run_full_proofing
6. Run post-proofing QA
7. Optionally export layer snapshots for visual review
8. Optionally run clean_deleted_layers only on explicit approval

## Frozen Layer Handling

Frozen layers are treated as fully hands-off across both apps:

- `list_layers` / `GET_LAYERS` never lists them, so they're never proposed for mapping
- `rename_unmapped_layers_to_deleted` and `unlock_layers_by_prefix` skip them
- The startup "unlock everything except protected booth layers" pass never unlocks a frozen layer
- Geometry/selection tools (`prepare_geometry`, `apply_layer_mappings`, `select_*`) already exclude frozen-layer entities natively via BricsCAD's own `ssget`/`SelectionSets.Select` behavior — no extra filtering needed there

## Troubleshooting

### Build fails with MSB3021/MSB3027 file lock

Cause: running BricsAI.McpServer or BricsAI.Overlay processes lock exe or plugin DLLs in bin/Debug.

Fix:

```powershell
Get-Process BricsAI.McpServer,BricsAI.Overlay -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln
```

### MCP connected but CAD actions fail

- Ensure BricsCAD is open with an active document
- Ensure server is not running in mock mode unless intentional
- Restart Claude MCP session after rebuilding binaries

### BricsAI.Overlay: agent responses look empty or proofing does nothing

Make sure you're on a build that includes the `AnthropicRuntime` response-parsing fix (v3.5.0+) — earlier builds silently discarded every Claude response due to an SDK content-block extraction bug, while still consuming real API tokens.
