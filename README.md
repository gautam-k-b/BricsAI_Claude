# BricsAI — Intelligent CAD Assistant & Multi-Agent Orchestrator
**v3.3.0** | .NET 9 · WPF · BricsCAD V15/V19 · Anthropic Claude Sonnet 4.5

BricsAI is an AI-powered multi-agent desktop application that automates complex exhibition CAD workflows directly inside **BricsCAD**. A "Mixture of Experts" LLM pipeline interprets natural-language prompts, extracts geometric relationships from the live drawing, and executes layer migrations, geometry cleanup, and quality-control checks via COM automation — eliminating repetitive drafting labor without ever requiring `NETLOAD` inside the CAD session.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                  BricsAI.Overlay (WPF)                  │
│  Chat UI · Quick Actions · Mapping Review Dashboard     │
│                                                         │
│  MainViewModel ──► Agent Pipeline                       │
│        │           Surveyor → Mapper → Executor → Validator
│        │                                                │
│        └──► ComClient (COM Automation)                  │
│                  PluginManager                          │
│                  ├── BricsAI.Plugins.V15Tools           │
│                  └── BricsAI.Plugins.V19Tools           │
└─────────────────────────────────────────────────────────┘
                          │  COM
                   ┌──────▼──────┐
                   │  BricsCAD   │
                   │  V15 / V19  │
                   └─────────────┘
```

**Solution Projects**

| Project | Role |
|---|---|
| `BricsAI.Overlay` | WPF UI, agent orchestration, COM client |
| `BricsAI.Core` | `IToolPlugin` interface, `KnowledgeService`, `LoggerService` |
| `BricsAI.Plugins.V15Tools` | Plugin implementations for BricsCAD V15 |
| `BricsAI.Plugins.V19Tools` | Plugin implementations for BricsCAD V19 |
| `BricsAI.TestRunner` | Headless integration test harness |
| `PluginTester` | Quick manual plugin smoke-tester |

---

## The Four AI Agents

### 1. Surveyor Agent — *The Eyes*
Runs before anything touches the drawing. Reads the raw layer list from BricsCAD via COM, compares it against the standard A2Z layer table and the persistent learned-mappings knowledge base, and outputs a plain-English summary. Layers that are not standard and not yet learned are tagged `[UNKNOWN]` — one tag per layer, with exact names, so the Mapper can act on them individually.

**Exact-match rule:** A layer is only considered standard if its name matches character-for-character. `Expo_BoothOutline MAX` is not `Expo_BoothOutline`.

### 2. Semantic Mapper Agent — *The Brain*
When the Surveyor tags unknown layers, the Mapper runs a two-phase pipeline:

**Phase 1 — Name Classification (`ClassifyByNameAsync`):** All unknown layer names are sent in a single LLM call. Layers with recognisable names are classified immediately; ambiguous names are flagged `UNCERTAIN`.

**Phase 2 — Geometry Batch (`BatchDeduceByGeometryAsync`):** Only `UNCERTAIN` layers are polled via `NET:POLL_LAYER_SEMANTICS` (entity type counts, block names, text samples). All footprints are then sent in one batched LLM call. The LLM returns a `{ "mappings": [...] }` JSON object covering every uncertain layer at once.

This reduces 100-layer drawings from ~100 LLM round-trips (~5 min) to 2 LLM calls (~36 sec).

**Human-in-the-Loop Review:** A dedicated `MappingReviewAgent` presents proposals **one at a time** in chat. Each proposal shows the source layer, proposed A2Z target, and a plain-English LLM-generated reason sentence. The drafter responds naturally — *"yes"*, *"skip"*, *"change X to Y"*, or *"abort"* — and `ClassifySingleMappingResponseAsync` classifies the intent as `ACCEPT`, `SKIP`, or `ABORT`. Accepted mappings are persisted via `NET:LEARN_LAYER_MAPPING`; skipped ones are bypassed; `ABORT` halts review and preserves all previously accepted mappings.

**Guard:** `NET:LEARN_LAYER_MAPPING` is blocked from executing inside any proofing batch (one that contains `APPLY_LAYER_MAPPINGS`). It is only valid as a direct response to an explicit user instruction.

### 3. Executor Agent — *The Hands*
Reads the user prompt and the Surveyor's summary and outputs a JSON array of `tool_calls` to run sequentially in BricsCAD. Follows a strict rule set that includes a hardcoded proofing sequence (Rules 7A–7E) and explicit guards against hallucinating commands.

### 4. Validator Agent — *The QA Manager*
After execution, reviews the transaction log and verifies that the requested operations actually completed. Reports discrepancies back to the chat and, on retry paths, feeds structured feedback to the Executor for a second attempt.

---

## Standard A2Z Target Layers

| Layer | Purpose |
|---|---|
| `Expo_BoothOutline` | Polyline boundaries of each exhibitor booth |
| `Expo_BoothNumber` | Text labels with booth numbers |
| `Expo_MaxBoothOutline` | Oversized / max-footprint booth outlines |
| `Expo_MaxBoothNumber` | Text labels for max-footprint booth numbers |
| `Expo_Building` | Walls, columns, doors, stairs, railings |
| `Expo_Column` | Structural column markers |
| `Expo_Markings` | Annotations, dimensions, aisle labels, title blocks |
| `Expo_View2` | Viewports, print-layout frames, utilities |
| `Expo_NES` | Non-exhibiting spaces (service areas, restrooms) |
| `Defpoints` | BricsCAD internal dimension points — never modified |
| `0` | BricsCAD default layer — never modified |

After a proofing run, `Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, and `Expo_MaxBoothNumber` are **locked** via `NET:LOCK_BOOTH_LAYERS` to protect them from subsequent operations.

---

## NET: Command Reference

All plugin capabilities are exposed through `NET:` prefix commands routed by `PluginManager` to the version-appropriate plugin DLL at runtime.

### Layer Tools (`LayerToolsPlugin`)

| Command | Description |
|---|---|
| `NET:GET_LAYERS:` | Returns a comma-separated list of all layers in the drawing |
| `NET:APPLY_LAYER_MAPPINGS` | Moves all objects to their mapped target layers using learned rules in `agent_knowledge.txt` |
| `NET:RENAME_DELETED_LAYERS` | Renames all non-standard, non-building layers to `Deleted_<originalName>` — marks them as retired without destroying geometry |
| `NET:DELETE_LAYERS_BY_PREFIX:<prefix>` | Permanently deletes all layers matching the prefix: unlocks, erases entities via LISP ssget + COM Paper_Space pass, then runs LAYDEL + PURGE. **Never used in a proofing sequence — only on explicit user request.** |
| `NET:ERASE_ENTITIES_ON_LAYER:<name>` | Erases all entities inside a named layer (model space ssget + Paper_Space COM pass). Does not delete the layer itself. |
| `NET:LOCK_BOOTH_LAYERS` | Locks `Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, `Expo_MaxBoothNumber` using exact COM layer name lookup |
| `NET:UNLOCK_LAYERS_BY_PREFIX:<prefix>` | Unlocks all layers whose names start with the given prefix |
| `NET:POLL_LAYER_SEMANTICS:<name>` | Returns a JSON object with entity type counts, block names, and text samples for the named layer — used by the Mapper for semantic deduction |
| `NET:LEARN_LAYER_MAPPING:<source>:<target>` | Saves a permanent mapping rule to `agent_knowledge.txt` via `KnowledgeService` |
| `NET:SELECT_LAYER:<source>:<target>` | Moves all objects on the source layer to the target layer via COM |
| `NET:SELECT_OUTER:<layer>` | Selects the geometrically outermost closed polyline on a layer |
| `NET:SELECT_INNER:<layer>` | Selects inner closed polylines on a layer |

### Geometry Tools (`GeometryToolsPlugin`)

| Command | Description |
|---|---|
| `NET:PREPARE_GEOMETRY` | Multi-pass recursive explosion pipeline: 9 global wipe cycles targeting all entity types not in the whitelist (Arc, Line, Circle, Ellipse, LWPolyline, Text, Solid), followed by deep block-dictionary traversal to erase nested geometry |
| `NET:SELECT_BOOTH_BOXES:<layer>` | Selects closed polylines on the named layer; used in proofing to highlight or move booth outlines |
| `NET:SELECT_EMPTY_BOOTHS:<targetLayer>` | Moves unnumbered booth outlines (outlines with no booth number text inside their polygon boundary) to the specified target layer; uses COM selection sets + ray-casting point-in-polygon |
| `NET:COUNT_EMPTY_BOOTHS` | **Read-only audit.** Counts booth outlines in `Expo_BoothOutline` that have no corresponding `Expo_BoothNumber` text inside them. Works on locked layers by using temporary unlocked COM selection sets. Returns: total outline count, text count, booths without a number, booths with a number |
| `NET:SELECT_COLUMNS:<layer>` | Selects column-like structural features on the named layer |
| `NET:SELECT_UTILITIES:<layer>` | Selects utility / viewport entities on the named layer |

---

## Standard Proofing Sequence (Rule 0–D)

When asked to proof a drawing the Executor always generates exactly this sequence — no additions, no substitutions:

```
0. NET:LOCK_BOOTH_LAYERS
     ↓ protect booth layers FIRST, before any geometry operations
A. NET:PREPARE_GEOMETRY
     ↓ recursive explosion, junk purge (booth layers untouched — they're locked)
B. NET:APPLY_LAYER_MAPPINGS
     ↓ migrate all vendor objects to A2Z layers
   [optional] NET:SELECT_BOOTH_BOXES, NET:SELECT_COLUMNS, NET:SELECT_UTILITIES
     ↓
C. (c:a2zcolor)
     ↓ enforce standard colors and lineweights
D. (command "-PURGE" "All" "*" "N")
     + NET:RENAME_DELETED_LAYERS
     ↓ retire any remaining unmapped layers as Deleted_
```

**ComClient proofing guards** enforce this sequence at the C# level regardless of what the LLM generates:
- If `APPLY_LAYER_MAPPINGS` appears without a preceding `LOCK_BOOTH_LAYERS` → auto-prepend lock at the start
- If `APPLY_LAYER_MAPPINGS` runs but `RENAME_DELETED_LAYERS` is absent → auto-appended after the batch
- `LEARN_LAYER_MAPPING` inside a proofing batch → silently skipped and logged as `GUARD`
- `DELETE_LAYERS_BY_PREFIX` immediately after `RENAME_DELETED_LAYERS` in the same batch → blocked

---

## Persistent Layer Knowledge (`agent_knowledge.txt`)

Mappings and rules are stored in `agent_knowledge.txt` next to the executable and read at runtime by `KnowledgeService.GetLearnings()` / `GetLayerMappingsDictionary()`. Both Surveyor and Executor prompts inject the full learned-rules block at call time, so the AI always sees the current state without recompilation.

Format:
```
[2026-03-12 22:11:25] Map the layer 'show_exhibit' to standard layer 'Expo_BoothOutline'.
[2026-03-12 22:11:25] Map the layer 'bldg_walls' to standard layer 'Expo_Building'.
[2026-03-12 23:32:30] If I ask to delete any layer and if it is locked, first unlock it and then delete it.
```

---

## UI Components

### Quick Actions Sidebar

| Button | What it does |
|---|---|
| **Run Full AI Proofing** | Fires the complete 5-step proofing sequence (A–E above) via natural language. Triggers the Surveyor → Mapper (with human review) → Executor → Validator pipeline. |
| **Clean Geometry** | Fast-path: unlocks all layers, runs RENAME_DELETED_LAYERS, then DELETE_LAYERS_BY_PREFIX to permanently remove all `Deleted_` layers, followed by multi-pass PURGE. Shows elapsed time on completion. |
| **Generate Summary** | Read-only audit: Surveyor enumerates layers and entity counts, Executor is instructed not to modify the drawing. |

> **Note:** All three Quick Action buttons are bound to `IsQuickActionsEnabled` (`!IsBusy && !_isInOneByOneMappingReview`). They are disabled for the entire duration of a one-by-one mapping review session to prevent accidental re-triggers. Only the chat **Send** button remains active so the drafter can respond to proposals.

### Chat Feed
- Real-time streaming of agent steps via `IProgress<string>` into live chat bubbles
- Performance summary (token total / input / output, elapsed time) displayed **before** the first mapping proposal
- One-by-one mapping review mode: each proposal includes a plain-English LLM-generated reason sentence; drafter responds per proposal with ACCEPT / SKIP / ABORT

---

## Layer Lifecycle

```
Vendor state:          "show_exhibit", "bldg_walls", "custom_A"
          │
          ▼  (Mapper + APPLY_LAYER_MAPPINGS)
Proofed state:         Expo_BoothOutline, Expo_Building, ...
          │
          ▼  (RENAME_DELETED_LAYERS — for any unmapped remainder)
Retired state:         Deleted_custom_A
          │
          ▼  (DELETE_LAYERS_BY_PREFIX — explicit user request only)
Removed state:         (layer gone)
```

---

## Performance & Cost

| Scenario | Before v3.3.0 | v3.3.0+ |
|---|---|---|
| Mapping 100 unknown layers | ~100 LLM calls, ~5 min | 2 LLM calls, ~36 sec |
| Prompt cache hit rate | Low (dynamic content mid-prompt) | High (dynamic content at bottom, stable prefix ≥ 1024 tokens) |
| Typical proofing cost | < $0.02 per run | < $0.02 per run (reduced further by cache discount) |

Anthropic prompt caching can be applied to stable prompt prefixes. Both `SurveyorAgent` and `ExecutorAgent` keep all dynamic content (`KnowledgeService.GetLearnings()`) at the **bottom** of their system prompts to maximise the reusable stable prefix.

---

## Setup & Configuration

### Prerequisites
- BricsCAD V15 or V19 (COM automation must be enabled)
- .NET 9.0 SDK
- An active Anthropic API key

### Running
1. Add your Anthropic key to `BricsAI.Overlay/appsettings.json`
2. Build: `dotnet build BricsAI.sln -c Release` or run `build.bat`
3. Launch BricsCAD
4. Run `BricsAI.Overlay.exe` — the overlay connects to the active BricsCAD instance automatically via COM

### Security
`appsettings.json` contains your API key. Do not commit it to source control. Rotate immediately if it is ever exposed.


BricsAI is an advanced, AI-powered multi-agent desktop application designed to orchestrate and execute complex workflow automation directly within **BricsCAD**. By leveraging a "Mixture of Experts" LLM pipeline, the application interprets user intent, programmatically extracts geometric relationships, and natively manipulates CAD layers and entities via COM automation to eliminate repetitive drafting labor.

---
