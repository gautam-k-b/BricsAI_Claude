# BricsAI — project context for Claude Code

A2Z exhibition floor-plan proofing for BricsCAD. .NET 9 solution. Current release: **v3.10.0** (Overlay 3.10.0, McpServer 1.1.0). Docs: `README.md`, `User_Guide.md`, `Claude_Code_MCP_Proofing_Guide.md`, `RELEASE_NOTES.md`.

## Architecture
- `BricsAI.Core` — `KnowledgeService` (SQLite at `%LOCALAPPDATA%\BricsAI\agent_knowledge.db`), `LoggerService`, `ProofingOptions`.
- `BricsAI.Plugins.V15Tools` / `V19Tools` — BricsCAD COM plugins. **Near-identical copies: every change goes into both.** Mirror V19 -> V15 with `sed 's/V19Tools/V15Tools/g;s/TargetVersion => 19/TargetVersion => 15/'`. Only LayerToolsPlugin, GeometryToolsPlugin, ExplodeTool*, SelfLearningTool* execute; the other per-version tools have `CanExecute => false` and only feed prompt examples to the Overlay.
- `BricsAI.McpServer` — stdio MCP server for Claude Code (STA COM host, no API key). Runs from `BricsAI.McpServer\bin\Release\net9.0-windows\`.
- `BricsAI.Overlay` — WPF app with its own Anthropic key (`appsettings.json`, gitignored). Agents: Intent -> Surveyor -> Mapper -> MappingReview -> Executor -> Validator; mapping grid with checkboxes and target drop-downs.
- Commands are `NET:...` strings dispatched to `IToolPlugin`; BricsCAD COM via `dynamic`; `doc.SendCommand` for LISP is asynchronous (needs sleeps). Mock CAD: `BRICSAI_MOCK_CAD=1` (its LISP interpreter only knows the shapes plugins emit).
- Proofing sequence is duplicated: MCP `ProofingTools.RunFullProofing` and Overlay `ComClient.RunFullProofingSequence`. Standard/booth layer sets are copy-pasted in ~8 places (KnowledgeService, MemoryTools, MainViewModel x2, plugins, ExplodeHelper, ComClient x2) — update all when adding a layer.
- `KnowledgeService.SetApplyFilter` is static state; clear it after a run.
- `build.bat` is stale (net8.0); use `dotnet build BricsAI.sln -c Release`.

## Rules that must hold
- **Explode never deletes drawing content.** Only POINT/3DFACE (upfront) and HATCH/ATTDEF that cannot be exploded are removed; layer 0 is not wiped, CLAYER not changed; other unexplodable entities stay and are reported as `WARNING:`.
- **Booth output layers are protected:** `Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, `Expo_MaxBoothNumber` (and `Proposed_BoothOutline/Number`) are locked during explode and are never automatic mapping targets. `SaveLearning` rejects booth targets; only the explicit reviewer path `SaveBoothMapping` / `GetBoothMappingsForApply` may use them.
- **Never push `Package/`, `ClientTestResults/`, `*.zip`, `.claude/settings.local.json`** (all in `.gitignore`). Work on a branch, not main. Do not commit/push unless asked. CRLF/LF warnings are harmless.
- Running processes lock DLLs: stop the Overlay and MCP server before a Release build (ask first), then restart.
- Treat the user's name/email/project as private; do not send to external services.

## Opt-in "block-grouped booths" mode (SEMICON-style drawings)
For drawings where booth outlines are connected inside block references and layers are non-standard (e.g. `SEMICON Japan 2027 OSSレイアウト_1102.dwg`: outlines on `01_一般`, numbers on `一般`). **Off by default** — it is for that kind of drawing only.
- Switch: `ProofingOptions.BlockGroupedBooths` — Overlay checkbox "Block-grouped booths (special drawings)", MCP `set_block_grouped_booths`, or env `BRICSAI_BLOCK_GROUPED_BOOTHS=1`.
- When on: `ExplodeHelper` uses `ExpLispBooth`/`WTypesBooth` (keeps closed heavy `POLYLINE`, whitelisted) and restores block contents from layer 0 to the parent layer (LISP `ba-xr`, one INSERT at a time); then `BoothProposer.Run` separates into `Proposed_BoothOutline`, `Proposed_BoothNumber`, `Proposed_Column`, `Proposed_Building` (nothing deleted; the two booth layers locked, never exploded).
- Detection: layer with >=20 same-size closed 4-vertex rectangles (0.25–4x dominant area) = outline source; >=20 booth-number texts (`^[A-Za-z]{0,2}\d{2,5}[A-Za-z]?$`, >=60% of its text) = number source. Thresholds were tuned on one drawing (mm units, 3000x3000 booths) — adjust if a drawing is missed.
- Overlay `MainViewModel` seeds grid suggestions: Proposed_BoothOutline->Expo_BoothOutline, Proposed_BoothNumber->Expo_BoothNumber, Proposed_Building->Expo_Building, Proposed_Column->Expo_Column (High confidence); `CompleteMappingReview` saves booth targets via `SaveBoothMapping`.
- Other pieces: `BoothMover` (`NET:MOVE_TO_BOOTH:<src>:<boothLayer>[:shapes|text|all]`, MCP `move_to_booth_layer`, `run_full_proofing boothMoves='src>layer:mode;…'`); fast `PollLayerSemantics` (native per-type counts + sampling capped at 3000 — per-entity COM iteration hung for ~20 min on large layers).
- **Not yet verified end-to-end on the live SEMICON drawing** after the Proposed_ redesign: check `ba-xr` explode+restore, the closed-polyline filter, the thresholds, and CHPROP with Japanese layer names. Use a copy of the original dwg (a previous run left booth numbers on `Deleted_一般` and outlines in `Expo_Building`).

## History / state
- Branch `fix/explode-no-auto-delete` pushed through `f909dba`, tag `v3.10.0`. No GitHub Release page created; not merged to main.
- `agent_knowledge.db` at repo root is a seed snapshot (1284 mappings, 16 rules), never read at runtime.
- `.claude/skills`: `bricsai-preflight` (read-only layer check) and `bricsai-proofing` (full AI proofing via MCP).
- `User_Guide.pdf` is stale (needs re-rendering from `User_Guide.md`).
- Known smells: stray debug dumps in repo root (AI_Context.txt, debug_log.txt, AI_*.json/txt); RELEASE_NOTES.md was once gitignored yet tracked.
