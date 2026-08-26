# BricsAI — User Guide

This guide explains how to install, configure, and use the BricsAI Overlay app to proof an exhibition floor plan drawing in BricsCAD.

---

## Contents

1. [What is the Overlay?](#what-is-the-overlay)
2. [Package Setup — First-Time Installation](#package-setup--first-time-installation)
3. [Runtime Requirements — Installing .NET 9](#runtime-requirements--installing-net-9)
4. [Configuring the API Key](#configuring-the-api-key)
5. [Before You Start](#before-you-start)
6. [The Screen Layout](#the-screen-layout)
7. [The Four Quick Action Buttons](#the-four-quick-action-buttons)
8. [The Mapping Review Table](#the-mapping-review-table)
9. [Step-by-Step Workflows](#step-by-step-workflows)
10. [Where Learned Mappings Are Stored](#where-learned-mappings-are-stored)
11. [Log Files — What They Are and Where to Find Them](#log-files--what-they-are-and-where-to-find-them)
12. [Frequently Asked Questions](#frequently-asked-questions)
13. [What the Chat Messages Mean](#what-the-chat-messages-mean)
14. [Troubleshooting](#troubleshooting)

---

## What is the Overlay?

The BricsAI Overlay is a chat-based desktop app that sits alongside BricsCAD. You click a button or type a request, and the app automatically cleans and organises your drawing using AI — renaming layers, exploding complex geometry, and checking booth numbers — without you needing to know any CAD commands.

---

## Package Setup — First-Time Installation

Your developer will provide a package folder (a zip or shared folder) containing the following files:

```
BricsAI.Overlay.exe
BricsAI.Overlay.dll
BricsAI.Core.dll
BricsAI.Plugins.V15Tools.dll
BricsAI.Plugins.V19Tools.dll
Anthropic.dll
Microsoft.Data.Sqlite.dll
SQLitePCLRaw.*.dll
System.*.dll
appsettings.json
agent_knowledge.db          ← the training/mapping database
```

### Step 1 — Copy the application files

Copy **all files except `agent_knowledge.db`** into any folder you choose on your machine, for example:

```
C:\BricsAI\
```

You can place this folder anywhere — the desktop, Documents, a network drive. The application does not need to be installed; just copy and run.

### Step 2 — Place the knowledge database

`agent_knowledge.db` must go in a **specific system location** that is separate from the application folder. This is where the app reads and writes all learned layer mappings.

Copy `agent_knowledge.db` from the package to:

```
C:\Users\<YourUsername>\AppData\Local\BricsAI\agent_knowledge.db
```

**Shortcut:** Open Windows Explorer, type `%LOCALAPPDATA%\BricsAI` in the address bar and press Enter. If the `BricsAI` folder does not exist, create it, then paste the file in.

> **Why a separate location?** This keeps your learned mappings safe when you receive a software update. The application files can be replaced entirely without touching your training data.

### Step 3 — Configure the API key

Open `appsettings.json` (in the same folder as the exe) in Notepad and fill in the API key your developer gave you:

```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-api03-...",
    "Model": "claude-sonnet-4-5-20250929"
  }
}
```

Save the file. Do not share this file or the key with anyone outside your team.

### Step 4 — Install the .NET 9 runtime (if prompted)

See the [Runtime Requirements](#runtime-requirements--installing-net-9) section below.

### Updating to a Newer Version

When your developer sends an updated package:

1. Stop the Overlay if it is running.
2. Replace **all application files** in your application folder with the new ones.
3. **Do not touch** `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` — your learned mappings live there and must not be replaced unless the developer specifically asks you to.

---

## Runtime Requirements — Installing .NET 9

The Overlay requires the **.NET 9 Windows Desktop Runtime**. This is a free Microsoft component. If it is not installed, Windows will show one of these messages when you try to run the app:

- *"To run this application, you must install .NET Desktop Runtime 9.x"*
- *"This application could not be started"*
- A dialog with a link titled **"Download it now"**

### How to install

1. Click **"Download it now"** in the error dialog (it takes you directly to the right installer), **or** go to:
   `https://dotnet.microsoft.com/download/dotnet/9.0`
   and download **.NET Desktop Runtime 9.0** for **Windows x64**.

2. Run the installer. It requires administrator rights — ask your IT team if you do not have them.

3. Once the installer completes, **launch `BricsAI.Overlay.exe` again** — you do not need to restart Windows.

> The runtime only needs to be installed once per machine. Future updates to BricsAI will not require reinstalling it.

---

## Configuring the API Key

The Overlay calls the Anthropic AI API on your behalf. Without a valid API key the AI buttons (Run Full AI Proofing, Generate Summary) will fail. The Clean Geometry and Explode Geometry buttons run natively and do not need the key.

The key is stored in `appsettings.json` next to the exe:

```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-api03-...",
    "Model": "claude-sonnet-4-5-20250929"
  }
}
```

- Do not modify the `"Model"` line unless your developer asks you to.
- If you receive a new key, replace only the `"ApiKey"` value, save the file, and restart the Overlay.

---

## Before You Start

1. **Open BricsCAD** and load the drawing you want to proof.
2. **Launch the Overlay** by double-clicking `BricsAI.Overlay.exe`.
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

Renames every non-standard layer with a `Deleted_` prefix, deletes them all, then runs a full PURGE on the drawing. No AI call is made — it runs instantly via native CAD commands.

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
6. Runs an iterative explode loop — as many passes as needed — until all remaining complex entities (blocks, MText, hatches, etc.) are resolved or confirmed unexplodable.

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

## Step-by-Step Workflows

### Proofing a Drawing for the First Time

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

### Exploding a Previously-Mapped Drawing

If you have already proofed a drawing before and just need to re-explode the geometry:

1. Open BricsCAD and load the drawing.
2. Launch the Overlay.
3. Click **💥 Explode Geometry**.
4. Watch the chat. The steps are shown as they run (unlock layers → lock booths → delete points/3D faces → flatten splines → explode loop).
5. When complete, the chat shows how many entities were resolved and whether any remained (unexplodable items like xrefs or dynamic blocks).
6. Save the drawing in BricsCAD.

---

## Where Learned Mappings Are Stored

Every layer mapping you confirm is saved automatically to a local SQLite database:

```
%LOCALAPPDATA%\BricsAI\agent_knowledge.db
```

Which expands to:

```
C:\Users\<YourUsername>\AppData\Local\BricsAI\agent_knowledge.db
```

This means:
- The same mappings are reused the next time you proof a drawing with the same vendor layers — no need to re-classify them.
- Mappings are updated in place (no duplicates accumulate).
- The store is shared between the Overlay app and any Claude MCP session running on the same machine.

> **Important:** If your developer sends you an updated `agent_knowledge.db` (for example, after a bulk training session), copy it to the path above and overwrite the existing file. Your previous mappings will be replaced with the updated ones.

---

## Log Files — What They Are and Where to Find Them

The Overlay writes two log files, both located in the **same folder as `BricsAI.Overlay.exe`**:

### `transaction_log.txt`

The primary operational log. It records every action the app takes, in chronological order. **This is the file to send your developer when something goes wrong.**

Each line looks like:

```
[2026-08-26 10:15:03] [SESSION]  Proofing started | File: MyShow.dwg | Prompt: Please proof this drawing...
[2026-08-26 10:17:44] [SESSION]  Proofing complete | File: MyShow.dwg | Tokens: 8420 total (6310 input, 2110 output) | Duration: 161.3s
[2026-08-26 10:17:44] [PLUGIN]   ApplyLayerMappings: remapping 'A-WALL' -> 'Expo_Building'
[2026-08-26 10:17:44] [BRICSCAD:SEND]   ...
```

Key tags you will see:
| Tag | What it means |
|---|---|
| `[SESSION]` | Start and end of a proofing run, with DWG filename and token count |
| `[USER]` | Message you typed |
| `[PLUGIN]` | Action the app took in BricsCAD |
| `[BRICSCAD:SEND]` | Command sent to BricsCAD |
| `[BRICSCAD:RECEIVE]` | Response received from BricsCAD |
| `[KNOWLEDGE]` | A mapping was saved or rejected |

### `chat_debug_log.txt`

A rolling transcript of every chat message from every proofing run, appended in order. Useful context for the developer alongside `transaction_log.txt`, but `transaction_log.txt` is always the primary file.

### How to find the log files

1. Open Windows Explorer.
2. Navigate to the folder where you put `BricsAI.Overlay.exe`.
3. The two log files (`transaction_log.txt` and `chat_debug_log.txt`) are in the same folder.

### What to send your developer

When reporting a problem, send both files:

- `transaction_log.txt`
- `chat_debug_log.txt`

Also note the name of the DWG file you were proofing and approximately what time the issue occurred — the developer can use that to find the right `[SESSION]` entry in the log.

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

**Q: I opened a new drawing but the chat still shows the old mapping table.**
The session resets automatically when you open a different drawing and send a new message or click a button. A divider line in the chat confirms the reset. No need to restart the Overlay.

**Q: My mapping seems to be ignored.**
Confirm that `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` exists and has not been overridden by a stale `BRICSAI_KNOWLEDGE_DIR` environment variable. The source layer name must match exactly (case is ignored, but the text must otherwise be identical).

**Q: The app fails immediately with an error about .NET or a missing runtime.**
Install the .NET 9 Windows Desktop Runtime — see [Runtime Requirements](#runtime-requirements--installing-net-9).

**Q: The app opens but AI buttons fail with an authentication error.**
The API key in `appsettings.json` is missing or incorrect — see [Configuring the API Key](#configuring-the-api-key). The Clean Geometry and Explode Geometry buttons still work without a valid key.

**Q: I received an updated package. Do I need to reinstall the runtime?**
No. The .NET runtime is installed on your machine independently of the app. Just replace the application files in your app folder and leave `agent_knowledge.db` in `%LOCALAPPDATA%\BricsAI\` untouched.

---

## What the Chat Messages Mean

| Icon / Text | Meaning |
|---|---|
| Spinning dots (…) | The app is working |
| ✅ | Step completed successfully |
| ⚠️ | Something was skipped or only partially completed — read the message |
| 📊 Performance: 0 API tokens | This step ran natively — no AI cost |
| 📊 Performance: N tokens (X input, Y output) | The AI was used; shows actual token consumption |
| ━━━ divider line ━━━ | Session was reset (new drawing detected, or a button was pressed while a review was open) |

---

## Troubleshooting

### The app crashes or shows a runtime error on first launch

Install the .NET 9 Windows Desktop Runtime. See [Runtime Requirements](#runtime-requirements--installing-net-9).

### The knowledge database was not found

If the app starts but says it cannot find previous mappings, the `agent_knowledge.db` file may be missing from `%LOCALAPPDATA%\BricsAI\`. Copy it from the package you received into that folder.

### AI proofing fails with "authentication" or "API key" error

Open `appsettings.json` (next to the exe), check that `"ApiKey"` is filled in correctly, save the file, and restart the Overlay.

### Claude cannot act on the drawing

- Ensure BricsCAD is open with an active drawing loaded.
- Ensure the Overlay has connected (you should see a greeting message).
- Close and reopen the Overlay if BricsCAD was launched after the Overlay.

### Something went wrong during proofing — how do I report it?

1. Note the name of the DWG file and the approximate time.
2. Navigate to the folder containing `BricsAI.Overlay.exe`.
3. Send your developer both `transaction_log.txt` and `chat_debug_log.txt`.

### The log files are very large

`transaction_log.txt` and `chat_debug_log.txt` grow over time. You can safely delete or archive them — the app creates new ones automatically on the next run. Do not delete them while the Overlay is running.
