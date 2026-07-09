# Training the BricsAI Proofing Agent on New Vendor Layers

Every expo venue and outside vendor uses their own proprietary layer naming conventions. For example, one vendor might use `l1xxxx` for their booth outlines, while another uses `Ve_Booth_Lines`. 

To ensure the AI Proofing Agent can automatically process these files without human intervention every time, the Agent is equipped with a **Permanent Memory Bank** stored in `agent_knowledge.txt`. 

You can train the AI to recognize these new vendor layers simply by talking to it in the BricsAI chat!

## How to Train the AI
When you receive a new file from a vendor and notice their layers don't match standard A2Z layers, simply open the drawing in BricsCAD and instruct the AI via the chat window to memorize the mapping.

### Example Training Prompts:
You can use natural language. The AI understands context and will automatically invoke its Memory Tool. Here are a few examples of exactly what to type into the chat:

* "Learn that the vendor layer `l1xxxx` should always be mapped to `Expo_BoothOutline`."
* "Memorize this rule for the future: `A-GLAZ` maps to `Expo_Building`."
* "Please remember that layer `S-COLS` is the same as `Expo_Column`."
* "Map the layer `Vendor_Booth_Text` to `Expo_BoothNumber` permanently."

### Bulk Training
If you have multiple layers to teach it from a brand new vendor, you can list them all in a single chat message!

> **Example:** 
> "I have a new vendor file. Please learn the following layer mappings:
> - `v_booth_lines` -> `Expo_BoothOutline`
> - `v_booth_txt` -> `Expo_BoothNumber`
> - `v_facility` -> `Expo_Building`"

## How It Works Behind the Scenes
When you send a training command, the AI evaluates your request and writes the rule to a local file named `agent_knowledge.txt` located in the BricsAI application directory. 

1. **Storage:** The rule is permanently saved. You do not need to repeat this training the next time you receive a file from that same vendor.
2. **Execution:** The next time you click **"Run Full AI Proofing"** on *any* drawing, the AI reads this memory bank first (via `KnowledgeService`). It checks the drawing for any layers listed in its memory, and automatically migrates all geometry to the correct A2Z standard layers before applying the final colors and purges.

## Managing the Memory Bank Manually
If you ever want to review what the AI has learned, or if you accidentally commanded it to learn a wrong layer, you can manually edit its brain!

1. Navigate to the folder where `BricsAI.Overlay.exe` is running (for example: `f:\Projects\BricsAI\BricsAI.Overlay\bin\Debug\net9.0-windows\`).
2. Open `agent_knowledge.txt` in Notepad or any text editor.
3. You will see human-readable lines such as:
   ```text
   [2026-02-27 10:15:32] Map the layer 'l1xxxx' to standard layer 'Expo_BoothOutline'.
   [2026-02-27 10:16:05] Map the layer 'S-COLS' to standard layer 'Expo_Column'.
   ```
4. You can delete or adjust individual mapping lines here. On the next run, BricsAI will parse this file and apply only the mappings that remain.
