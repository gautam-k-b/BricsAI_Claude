# BricsAI MCP Server for BricsCAD

BricsAI is now an MCP-first local automation stack for BricsCAD proofing workflows.

The server runs locally on each user machine and is consumed from Claude Code or Claude Desktop over stdio. It exposes deterministic CAD tools (layer mapping, geometry preparation, proofing, audit, and cleanup) backed by COM automation and version-specific plugin DLLs.

## Quick Documents

- End-user one-page SOP: End_User_SOP.md
- Developer onboarding: Developer_Onboarding.md
- Detailed training playbook: Training_Guide.md
- Release history: RELEASE_NOTES.md

## Current Architecture

```text
Claude Code / Claude Desktop
          |
          | MCP stdio
          v
BricsAI.McpServer (Host + Tool Surface + Prompt Surface)
          |
          | STA COM bridge
          v
ComClient -> PluginManager -> BricsAI.Plugins.V15Tools / BricsAI.Plugins.V19Tools
          |
          v
BricsCAD ActiveDocument (V15/V19)
```

### Key Runtime Components

- BricsAI.McpServer
  - Hosts MCP over stdio
  - Exposes tools and prompts from assembly
  - Forces COM execution through a dedicated STA thread
- BricsAI.Core
  - IToolPlugin contract
  - KnowledgeService for persistent mappings and rules
  - LoggerService for transaction and MCP usage logs
- BricsAI.Plugins.V15Tools
  - BricsCAD V15 command implementations
- BricsAI.Plugins.V19Tools
  - BricsCAD V19 command implementations

## Main MCP Tool Groups

### Layer tools

- list_layers
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

### Proofing tools

- run_full_proofing
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

Persistent mapping/rule memory is stored in agent_knowledge.txt next to the executing server binary.

Recent improvements:

- startup compaction removes stale duplicate mapping entries
- write-path deduplicates mapping rules by source layer
- read-path returns deduplicated latest mapping set
- cached parse avoids repeated full-file parsing when unchanged

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

## Recommended End User Flow

1. Run read-only preflight summary
2. Auto-classify unmapped layers
3. Escalate ambiguous layers with get_layer_geometry paging
4. Apply approved/high-confidence mappings
5. Run run_full_proofing
6. Run post-proofing QA
7. Optionally export layer snapshots for visual review
8. Optionally run clean_deleted_layers only on explicit approval

## Troubleshooting

### Build fails with MSB3021/MSB3027 file lock

Cause: running BricsAI.McpServer processes lock exe or plugin DLLs in bin/Debug.

Fix:

```powershell
Get-Process BricsAI.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln
```

### MCP connected but CAD actions fail

- Ensure BricsCAD is open with an active document
- Ensure server is not running in mock mode unless intentional
- Restart Claude MCP session after rebuilding binaries
