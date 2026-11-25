# Running a Full AI Proofing from Claude Code (BricsAI MCP)

This guide walks you through opening Claude Code in a console, listing the MCP servers, registering and starting the BricsAI MCP server, and prompting Claude to run a full AI proofing on the drawing open in BricsCAD.

---

## 1. Before you start

| Requirement | Check |
|---|---|
| BricsCAD (V15 or V19) is **open** with the drawing to proof as the **active document** | Required |
| Claude Code is installed and you are signed in (`claude` runs without a login prompt) | Required |
| BricsAI.McpServer is built (Release) | `BricsAI.McpServer\bin\Release\net9.0-windows\BricsAI.McpServer.exe` exists |
| The `BRICSAI_MOCK_CAD` environment variable is **not** set (unless you intend to test against the mock drawing) | Recommended |

If the exe does not exist, build it once from the project folder:

```powershell
dotnet build BricsAI.sln -c Release
```

> Close any running `BricsAI.McpServer` / `BricsAI.Overlay` processes before rebuilding, or the build fails with a file-lock error (MSB3021/MSB3027).

---

## 2. Start Claude Code

1. Open **PowerShell** or **Windows Terminal**.
2. Go to the project folder:

   ```powershell
   cd C:\Users\gbhowmik\Documents\BricsAI_Claude
   ```
3. Type `claude` and press Enter:

   ```powershell
   claude
   ```

You are now in the Claude Code console. Lines starting with `/` are slash commands; anything else is a normal prompt to Claude.

---

## 3. List the existing MCP servers

You can do this two ways.

**From inside Claude Code (interactive):**

```
/mcp
```

This opens a panel showing every configured MCP server and its status (connected / failed / needs authentication). Use the arrow keys to select a server to see its tools.

**From the normal terminal (outside Claude Code):**

```powershell
claude mcp list
```

Details of one server:

```powershell
claude mcp get bricsai
```

Look for a server named **`bricsai`** with status **Connected**. If it is listed, skip to section 5. If it is not listed, register it in section 4.

---

## 4. Register the BricsAI MCP server (one-time setup)

Run this in a normal terminal (not inside Claude Code). Adjust the path if your folder differs.

```powershell
claude mcp add --scope local --transport stdio bricsai -- "C:\Users\gbhowmik\Documents\BricsAI_Claude\BricsAI.McpServer\bin\Release\net9.0-windows\BricsAI.McpServer.exe"
```

Scope options:

| Scope | Meaning |
|---|---|
| `local` | Only you, only this project (default) |
| `project` | Saved in `.mcp.json` and shared with the team via git |
| `user` | Available to you in every project |

Verify:

```powershell
claude mcp get bricsai
```

To remove or re-add it (for example after moving the folder):

```powershell
claude mcp remove bricsai
```

---

## 5. Start the MCP server

You do **not** start the server by hand. Claude Code launches the registered `bricsai` exe automatically (over stdio) when a session starts. To start it:

1. Start (or restart) Claude Code: `claude`
2. Type `/mcp` and confirm `bricsai` shows **Connected**.

If it shows **Failed**:

- Make sure BricsCAD is open with an active drawing.
- Make sure the exe path from section 4 is correct.
- If you rebuilt the binaries, exit Claude Code (`/exit`) and start it again. The server is only reloaded on a new session.
- In `/mcp`, select `bricsai` and choose **Reconnect**.

To confirm the tools are available, ask Claude:

```
List the tools available from the bricsai MCP server.
```

You should see tools such as `list_layers`, `get_unmapped_layers`, `apply_layer_mappings`, `prepare_geometry`, `run_full_proofing`, and `clean_deleted_layers`.

---

## 6. Run a full AI proofing (prompts)

Paste these prompts one at a time. Claude asks for permission the first time it uses each tool. Choose **Yes** (or "Yes, don't ask again" for `bricsai` tools if you trust the workflow).

### Option A: Guided proofing with review gates (recommended)

This uses the project's `bricsai-proofing` skill. It proposes layer mappings, waits for your approval, and only then proofs.

**Step 1: Read-only preflight (nothing is changed)**

```
/bricsai-proofing read-only
```

Or in plain words:

```
Using the bricsai MCP server, run a read-only preflight on the active drawing. List all layers, show which are unmapped, and show the learned mappings. Do not modify the drawing.
```

**Step 1b: Explode geometry first**

XRef and block geometry sits on collapsed layers until it is exploded, so explode before analysing layers. Booth output layers are locked automatically.

```
Run explode_with_booth_lock, then list the layers again so we analyse the full expanded layer set.
```

**Step 2: Propose mappings**

```
Analyze the unmapped layers. For each one, propose the best-fit A2Z target layer (Expo_Building, Expo_Column, Expo_View2, Expo_Markings, Expo_NES, Expo_ImageMarkings, or none). For ambiguous layers, inspect the geometry before deciding. Show the result as a table with columns: Source layer, Proposed target, Confidence, Reason.
```

**Step 3: Review and correct (you stay in control)**

```
Map "VENDOR_WALLS" to Expo_Building. Exclude "TEMP_NOTES". Remember these choices for next time.
```

**Step 4: Confirm and run**

```
Approved. Apply the approved mappings, then run the full proofing with skipPrepareGeometry=true (geometry was already exploded).
```

(To stop at any point, type `Cancel. Do not proof.`)

**Step 5: Audit and report**

```
Run a post-proofing audit: confirm the booth layers are intact, check for empty booths and booths missing numbers, and list any entities that could not be exploded. Then give me a final proofing report.
```

**Step 6: Optional cleanup (only when you are happy with the result)**

```
Run clean_deleted_layers to remove the layers renamed to deleted.
```

### Option B: One-shot proofing

Use this only when the layer mappings are already learned and trusted.

```
Using the bricsai MCP server, run a full AI proofing on the active drawing: first run explode_with_booth_lock, then check for unmapped layers, apply the learned layer mappings, run run_full_proofing with skipPrepareGeometry=true, then audit the result and give me a summary report. Stop and ask me before applying any mapping you are not confident about.
```

### Special drawings: booths joined inside blocks (SEMICON-style)

Off by default. Only for drawings whose booth outlines are connected inside block references.

```
Using the bricsai MCP server, call set_block_grouped_booths with enabled=true, then run explode_with_booth_lock. Show me the Proposed_BoothOutline / Proposed_BoothNumber / Proposed_Building / Proposed_Column result, then continue the proofing with those mapped to Expo_BoothOutline / Expo_BoothNumber / Expo_Building / Expo_Column after I confirm.
```

Use `move_to_booth_layer` (or `boothMoves` on `run_full_proofing`) for explicit booth moves. Turn the option off afterwards with `set_block_grouped_booths` enabled=false (it also resets when the server restarts).

### Useful follow-up prompts

| Goal | Prompt |
|---|---|
| Count booths with no number | `Count the empty booths and select them.` |
| Inspect one layer | `Show me the geometry on layer "XYZ" and export a snapshot of it.` |
| Teach a rule | `Remember: always map layers starting with "BLDG_" to Expo_Building.` |
| See what was learned | `Show the learned mappings.` |
| Hand-off summary | `Summarize what you changed in this session.` |

---

## 7. Useful Claude Code commands

| Command | What it does |
|---|---|
| `/mcp` | List and manage MCP servers |
| `/help` | Show all commands |
| `/clear` | Start a fresh conversation (resets context) |
| `/exit` or `Ctrl+C` twice | Quit Claude Code |
| `Esc` | Interrupt Claude mid-task |
| `claude mcp list` | List MCP servers (outside the session) |
| `claude mcp get bricsai` | Show details and status of the BricsAI server |
| `claude mcp remove bricsai` | Unregister the server |

---

## 8. Troubleshooting

| Symptom | Fix |
|---|---|
| `bricsai` not listed | Run the `claude mcp add` command in section 4 |
| `bricsai` shows Failed | Check the exe path, open BricsCAD with a drawing, restart Claude Code |
| Tools run but nothing changes in CAD | Confirm BricsCAD has an active document and `BRICSAI_MOCK_CAD` is not set |
| Changes not picked up after a rebuild | `/exit`, then run `claude` again |
| Build error MSB3021/MSB3027 | `Get-Process BricsAI.McpServer,BricsAI.Overlay -ErrorAction SilentlyContinue \| Stop-Process -Force`, then rebuild |
| Want to see what the server did | Check `BricsAI.McpServer\bin\Release\net9.0-windows\transaction_log.txt` |

---

## 9. Good practice

- Always run the read-only preflight first.
- Review the mapping table before approving. Vendor layers must never be mapped onto the booth output layers (`Expo_BoothOutline`, `Expo_BoothNumber`, `Expo_MaxBoothOutline`, `Expo_MaxBoothNumber`). The server rejects these.
- Frozen layers are never touched.
- Save a copy of the drawing before the first proofing run on a new file.
- Learned mappings are stored in `%LOCALAPPDATA%\BricsAI\agent_knowledge.db` and are shared with the BricsAI Overlay app.
