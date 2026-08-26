# BricsAI Overlay — User Guide

This guide explains how to use the BricsAI Overlay app to proof an exhibition floor plan drawing in BricsCAD.

---

## What is the Overlay?

The BricsAI Overlay is a chat-based desktop app that sits alongside BricsCAD. You click a button or type a request, and the app automatically cleans and organises your drawing using AI — renaming layers, exploding complex geometry, and checking booth numbers — without you needing to know any CAD commands.

---

## Before You Start

1. **Open BricsCAD** and load the drawing you want to proof.
2. **Launch the Overlay** by running `BricsAI.Overlay.exe` (your team will give you this file).
3. The Overlay connects to BricsCAD automatically. You will see a greeting message in the chat area on the right.

---

## The Screen Layout

```
┌──────────────────┬──────────────────────────────────────┐
│  Quick Actions   │                                      │
│  (left sidebar)  │         Chat Area (right)            │
│                  │                                      │
│  🤖 Run Full AI  │  Messages appear here as the app     │
│  🧹 Clean Geom.  │  works through your drawing.         │
│  💥 Explode Geom │                                      │
│  📊 Generate Sum │                                      │
│                  │──────────────────────────────────────│
│                  │  Type here and press Enter           │
└──────────────────┴──────────────────────────────────────┘
```

All four buttons on the left are disabled while the app is working, so you cannot accidentally start two things at once.

---

## The Four Quick Action Buttons

### 🤖 Run Full AI Proofing
**Use this for a complete drawing proof.**

This is the main button. It runs the full sequence:
1. Surveys all layers in the drawing.
2. Matches vendor layer names to the standard A2Z layer scheme using AI.
3. Shows you a **review table** (see below) so you can confirm or adjust before anything changes.
4. After you confirm, applies the layer mappings, cleans geometry, and validates the result.

Use this at the start of every job.

---

### 🧹 Clean Geometry
**Use this to purge junk layers and unused objects.**

Deletes all layers prefixed `Deleted_` and runs a full PURGE on the drawing. No AI call is made — it runs instantly via native CAD commands.

Use this after proofing if you want to do a final tidy-up.

---

### 💥 Explode Geometry
**Use this to explode complex entities without running full proofing.**

Runs a standalone explode pass in this exact order:
1. Unlocks every layer in the drawing (except the four protected booth layers).
2. Locks the four booth output layers so they are never touched: `Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, `Expo_MaxBoothNumber`.
3. Deletes all POINT entities (they cannot be exploded and serve no purpose).
4. Deletes all 3D Face entities (they cannot be exploded; only deletion works).
5. Flattens all SPLINE entities (converts them to polylines).
6. Runs an iterative explode loop — up to 30 passes — until all remaining complex entities (blocks, MText, hatches, etc.) are resolved.

The chat shows each step and the final result. No API tokens are used — this runs natively.

Use this when you only need to clean geometry without doing a full proof (for example, on a drawing you have already mapped before).

---

### 📊 Generate Summary
**Use this for a read-only audit — no changes to the drawing.**

Asks the AI to look at the drawing's layer and booth data and generate a Bill of Materials or audit summary. Nothing is moved or deleted.

Use this when someone asks "what's in this drawing?" without wanting to proof it yet.

---

## The Mapping Review Table

When you click **Run Full AI Proofing**, after the AI has classified all the layers, a table appears in the chat like this:

```
#  | Source Layer      | Target Layer        | Confidence | Action
---|-------------------|---------------------|------------|--------
1  | A-WALL            | Expo_Building       | High       | Include
2  | V_BOOTH_LINES     | Expo_BoothOutline   | High       | Include
3  | MISC_STUFF        | Deleted_MISC_STUFF  | Low        | Pending
4  | UNKNOWN_1         | Deleted_UNKNOWN_1   | Low        | Pending
```

**Proofing does not start until you confirm this table.** You are always in control.

### What you can type in reply:

| What you type | What happens |
|---|---|
| `include 1,2` | Includes rows 1 and 2; all other pending rows are auto-excluded |
| `exclude 3` | Excludes row 3; all other pending rows are auto-included |
| `include 1,2 and exclude 3` | Applies both at once; anything else is auto-excluded |
| `process high confidence only` | Includes all High rows, excludes all Low rows |
| `confirm` / `yes` / `looks good` | Confirms whatever is decided and starts proofing |
| `confirm all` | Includes every row and starts proofing immediately |
| `remember A-WALL always maps to Expo_Building` | Saves this as a permanent rule; review stays open |
| `stop` / `cancel` | Cancels — nothing is changed in the drawing |

**Tip:** The safest default for a first run is to type `process high confidence only` then `confirm`. High-confidence rows are almost always correct. You can deal with low-confidence layers manually afterwards.

---

## Step-by-Step: Proofing a Drawing for the First Time

1. Open BricsCAD and load your drawing file.
2. Launch the Overlay. Wait for the greeting message.
3. Click **🤖 Run Full AI Proofing**.
4. The app surveys the drawing and classifies layers. Watch the chat — each phase is shown as it runs.
5. A mapping review table appears. Read through it.
6. Type `process high confidence only` and press Enter.
7. The table updates. Check that the included rows look right.
8. Type `confirm` and press Enter.
9. Proofing runs: layers are renamed, geometry is cleaned, and a validation check is done. The chat shows each step.
10. When the green tick appears, the drawing is proofed. Save it in BricsCAD.

---

## Step-by-Step: Exploding a Previously-Mapped Drawing

If you have already proofed a drawing before and just need to re-explode the geometry:

1. Open BricsCAD and load the drawing.
2. Launch the Overlay.
3. Click **💥 Explode Geometry**.
4. Watch the chat. The steps are shown as they run (unlock layers → lock booths → delete points/3D faces → flatten splines → explode loop).
5. When complete, the chat shows how many entities were resolved and whether any remained (unexplodable items like xrefs or dynamic blocks).
6. Save the drawing in BricsCAD.

---

## Frequently Asked Questions

**Q: Can I type my own requests in the chat?**
Yes. The text box at the bottom accepts natural language. For example: "Show me all booth outlines that are missing a number" or "Move everything on layer MISC to Deleted_MISC."

**Q: Will the app change my drawing without asking?**
No. The mapping review table always appears before any destructive step. Proofing never starts automatically.

**Q: What are the four protected booth layers?**
`Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, and `Expo_MaxBoothNumber`. These are always locked before any explode or cleanup operation so they can never be accidentally modified.

**Q: The button is greyed out — why?**
The app is currently working. All buttons lock while a task is running and unlock automatically when it finishes.

**Q: The app connected but nothing is happening in BricsCAD.**
Make sure BricsCAD is open with a drawing loaded (not just the application open with no file). Then try your action again.

**Q: I made a mistake during the review and want to start over.**
Type `stop` or `cancel` to abort the review without changing the drawing. Then click the proofing button again to start fresh.

---

## What the Chat Messages Mean

| Icon | Meaning |
|---|---|
| Spinning dots (…) | The app is working |
| ✅ | Step completed successfully |
| ⚠️ | Something was skipped or only partially completed — read the message |
| 📊 Performance: 0 API tokens | This step ran natively — no AI cost |
| 📊 Performance: N tokens | The AI was used for this step |
