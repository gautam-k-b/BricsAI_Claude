# BricsAI Training Guide for Claude MCP Users

This guide is for end users running BricsAI.McpServer locally with Claude Code or Claude Desktop.

## What "training" means

Training in BricsAI means teaching persistent layer mappings from vendor layer names to A2Z standard layers.

Examples:

- vendor layer A-WALL maps to Expo_Building
- vendor layer V_BOOTH_TEXT maps to Expo_BoothNumber

These mappings are saved in a local SQLite database and reused automatically in later proofing runs — and are also immediately available if you switch to the BricsAI.Overlay desktop app, since both share the same store.

## Where memory is stored

Knowledge is stored in a SQLite database at:

- `%LOCALAPPDATA%\BricsAI\agent_knowledge.db`

(not next to the server executable anymore — this is a per-user location shared by both BricsAI.McpServer and BricsAI.Overlay). A starter copy is checked into the project repo at the solution root; if you're setting up a new machine, copy that file to the path above before your first run to carry over the existing mapping history instead of starting empty.

Current behavior:

- mappings are updated in place per source layer (no duplicates ever accumulate)
- latest mapping for each source layer is retained
- lookups are indexed, so performance stays flat as the mapping history grows

## Basic user workflow

1. Open BricsCAD and load a drawing
2. Open Claude (Code or Desktop) with bricsai MCP connected
3. Ask for a read-only summary
4. Ask Claude to classify unmapped layers
5. Approve and apply mappings
6. Run full proofing
7. Optionally clean Deleted_ layers only after explicit confirmation

## Simple prompts for non-technical users

### Check connection and drawing access

- Confirm bricsai MCP is connected and that you can access the active BricsCAD document.

### Read-only preflight

- Generate a read-only summary of this drawing, including standard layers, unmapped layers, and booths missing numbers. Do not modify the drawing.

### Teach one mapping

- Learn that layer A-WALL should always map to Expo_Building.

### Teach multiple mappings at once

- Learn these mappings permanently:
  - V_BOOTH_LINES -> Expo_BoothOutline
  - V_BOOTH_TEXT -> Expo_BoothNumber
  - V_BUILDING -> Expo_Building

### Auto-classify uncertain layers with deeper evidence

- Classify all unmapped layers. Use name and semantics first. If still ambiguous, use get_layer_geometry with pagination before proposing mappings.

### Run full proofing

- Run full proofing now using the standard deterministic workflow.

### Export visual snapshots for review

- Export layer snapshots for Expo_BoothOutline, Expo_BoothNumber, Expo_Building, and Expo_View2 and return file paths.

## Advanced mapping quality workflow

Use this when vendor layers are noisy or inconsistent:

1. get_unmapped_layers
2. poll_layer_semantics for each uncertain layer
3. get_layer_geometry with paging for unresolved ambiguity
4. propose mapping plus one-line reason
5. apply only high-confidence mappings
6. keep low-confidence layers for manual review

**If using BricsAI.Overlay instead of MCP**, the above happens automatically via the 3-phase pipeline:

- Phase 1: all unknown layers classified by name in batches of 40 (confident → table; ambiguous → Phase 2)
- Phase 2: geometry polled for ambiguous layers, then batch-classified (low-confidence → Phase 3)
- Phase 3: snapshot exported and visually verified by Claude Vision

The tabular review is always shown — even when the drawing's layers are fully known from previous sessions — so you always confirm before proofing touches the drawing.

**Overlay mapping-review commands:**

| Command | What it does |
|---|---|
| `include 1,3,5` | Includes those rows; auto-excludes all others |
| `exclude 2,4` | Excludes those rows; auto-includes all others |
| `include 1,3 and exclude 2,4` | Both lists applied; anything else auto-excluded |
| `process high confidence only` | Bulk-includes High rows, bulk-excludes Low rows |
| `confirm` / `yes` / `looks good` | Confirms the current decided state and starts proofing |
| `confirm all` | Includes all remaining pending rows and starts proofing |
| `remember X maps to Y` | Saves rule mid-review; review stays open |
| `stop` / `cancel` | Cancels the review; proofing does not start |

## Understanding usage logs and token estimates

Every MCP tool action now logs usage details in transaction_log.txt, including:

- timestamp
- action and action type
- CAD file name/path
- input/output character counts
- estimated input/output tokens
- elapsed time

Important: token counts are estimated, not billing-accurate Claude usage values.

## Troubleshooting

### Build fails with file lock errors

If build fails with MSB3021 or MSB3027, stop running MCP server processes and build again:

```powershell
Get-Process BricsAI.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln -c Release
```

### Claude cannot act on drawing

- Ensure BricsCAD is open with an active drawing
- Ensure bricsai MCP is connected in Claude
- Restart Claude MCP session after rebuilding binaries

### Mapping seems ignored

- Confirm `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` exists and isn't overridden by a stale `BRICSAI_KNOWLEDGE_DIR` environment variable
- Ask Claude to run get_learned_mappings and confirm the source layer name matches exactly (case doesn't matter, but the text must otherwise match)
