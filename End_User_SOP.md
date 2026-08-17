# BricsAI End-User SOP (One Page)

Purpose: run reliable CAD proofing with Claude + BricsAI MCP using a safe, repeatable process.

This SOP covers the **Claude Code / Claude Desktop + BricsAI MCP** flow. If you're using the **BricsAI.Overlay** desktop app instead, the daily workflow is the same (open BricsCAD, open the drawing, ask it to proof) but the mapping-review step looks different: Overlay shows one numbered table of all proposed mappings (with a confidence and reason column) instead of a conversational back-and-forth, and you reply with things like `include 1,3,5`, `exclude 2,4`, or `confirm all` — you can also say `remember <layer> always maps to <target>` at any point during review without interrupting it.

## 1) Daily Startup

1. Open BricsCAD.
2. Open the target drawing.
3. Start Claude Code or Claude Desktop in the project where BricsAI MCP is configured.
4. Confirm MCP connectivity.

Prompt:

"Confirm bricsai MCP is connected and can access the active BricsCAD document."

## 2) Safe Preflight (Read-Only)

Run a no-change diagnostic first.

Prompt:

"Generate a read-only summary of this drawing: standard layers, unmapped layers, booth-number issues, and obvious geometry risks. Do not modify the drawing."

## 3) Mapping Workflow

1. Request unmapped layers.
2. Ask Claude to classify using names and semantics.
3. For ambiguous layers, escalate to geometry paging.
4. Approve only high-confidence mappings.
5. Apply mappings.

Prompt:

"Classify all unmapped layers. Use name and semantics first. If uncertain, use get_layer_geometry with pagination before proposing a mapping."

Note: frozen layers never appear in this list — they're intentionally left untouched throughout proofing. If a layer you expect to see is missing, check whether it's frozen.

## 4) Proofing Run

Prompt:

"Run full proofing using deterministic workflow and provide a concise result summary."

Expected results:

- booth numbering consistency checks
- non-standard entity checks
- cleanup recommendations

## 5) Optional Visual QA

Export snapshots for manual review.

Prompt:

"Export layer snapshots for Expo_BoothOutline, Expo_BoothNumber, Expo_Building, and Expo_View2. Return file paths."

## 6) Controlled Cleanup

Do destructive cleanup only after explicit confirmation.

Prompt:

"Before deleting or renaming anything, show exactly what will change and wait for my approval."

## 7) End-of-Run Checklist

- Save the drawing.
- Record key mapping decisions in your team notes.
- If new vendor layers were seen, teach durable mappings.

Prompt:

"Learn these mappings permanently: <source> -> <target>."

## 8) Troubleshooting Quick Fixes

Build lock symptoms (MSB3021/MSB3027):

```powershell
Get-Process BricsAI.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build BricsAI.sln -c Release
```

If Claude cannot act on CAD:

- Ensure BricsCAD is open with an active document.
- Confirm MCP is connected.
- Restart the Claude MCP session after rebuilding.

## 9) Log Awareness

Usage logs include action, CAD file, character counts, estimated tokens, and elapsed time.

Location:

- BricsAI.McpServer/bin/<Configuration>/net9.0-windows/transaction_log.txt

Note: token values are estimates, not billing-accurate provider tokens.
