# Release Notes - BricsAI

## v3.4.0 - MCP-First Claude Integration Update
Released: 2026-07-23

This release aligns the product with local MCP usage from Claude Code and Claude Desktop, introduces deeper layer inspection capabilities, adds per-action usage logging with CAD context, and removes the legacy overlay project from the solution.

### Major Architecture Changes

- Removed BricsAI.Overlay project from repository and build graph
- Standardized on BricsAI.McpServer as the primary runtime entrypoint
- Documentation and training flow updated for Claude MCP operation

### New MCP Tooling

#### 1) Detailed geometry extraction with paging

Added get_layer_geometry with entity-level JSON output and pagination controls.

- Input: layer, offset, maxEntities
- Output metadata:
  - TotalCount
  - ReturnedCount
  - Truncated
  - NextOffset
- Supports incremental reads for large layers to avoid oversized context payloads

#### 2) Layer snapshot export

Added export_layer_snapshot for visual layer output to temp files.

- Input: layer, format
- Output includes:
  - File path
  - Format requested
  - Format used
  - Entity count
- Falls back to BMP when requested export format is unavailable

### Mapping Prompt Improvements

Prompt guidance now explicitly escalates uncertain classifications:

1. poll_layer_semantics
2. get_layer_geometry with pagination when ambiguity remains

This improves confidence for difficult vendor-layer mappings while preserving deterministic behavior.

### Knowledge Store Reliability and Scale

KnowledgeService enhancements:

- Startup compaction of agent_knowledge.txt
- Mapping deduplication on write (latest mapping wins)
- Deduplicated read output for prompt context
- In-memory cache keyed by file write-time to reduce repeated parse overhead

Effect: stable long-term behavior for local users with growing mapping history.

### MCP Usage Logging with CAD Context

Added per-action usage telemetry logging in transaction_log.txt.

Each tool action now logs:

- timestamp
- action type (NET or RAW)
- action text
- active CAD document name/path
- input/output character counts
- estimated input/output token counts
- elapsed time in ms

Note: token values are estimated from text length and are not billing-accurate provider tokens.

### Build and Runtime Notes

- Build failures like MSB3021/MSB3027 are typically file-lock conflicts from running BricsAI.McpServer processes
- Recommended build sequence:

```powershell
Get-Process BricsAI.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln -c Release
```

### Compatibility

- BricsCAD V15 and V19 plugin paths remain supported
- Claude Code local-scope MCP registration remains supported
- Claude Desktop stdio MCP integration remains supported

---

## v3.3.1 and Earlier

Historical notes prior to this migration centered on the overlay multi-agent UX and progressive proofing reliability upgrades. For current deployments, use v3.4.0 behavior and README guidance as the source of truth.
