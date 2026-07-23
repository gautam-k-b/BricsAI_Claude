# BricsAI Training Guide for Claude MCP Users

This guide is for end users running BricsAI.McpServer locally with Claude Code or Claude Desktop.

## What "training" means

Training in BricsAI means teaching persistent layer mappings from vendor layer names to A2Z standard layers.

Examples:

- vendor layer A-WALL maps to Expo_Building
- vendor layer V_BOOTH_TEXT maps to Expo_BoothNumber

These mappings are saved in agent_knowledge.txt and reused automatically in later proofing runs.

## Where memory is stored

Knowledge is stored in agent_knowledge.txt next to the server executable that is running.

Typical path:

- BricsAI.McpServer/bin/Release/net9.0-windows/agent_knowledge.txt

Recent behavior improvements:

- duplicate mappings are compacted and deduplicated automatically
- latest mapping for each source layer is retained
- read-path is optimized for faster repeated access

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

- Check agent_knowledge.txt path for the server binary actually running
- Confirm the mapping line exists and source layer name matches exactly
