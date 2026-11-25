---
description: Read-only preflight of the active BricsCAD drawing. Lists all layers, shows unmapped layers and learned mappings. Use when the user asks for a preflight, layer check, or layer summary. Never modifies the drawing.
allowed-tools: mcp__bricsai__list_layers, mcp__bricsai__get_unmapped_layers, mcp__bricsai__get_learned_mappings
---

# BricsAI Read-Only Preflight

Using the bricsai MCP server, run a read-only preflight on the active drawing.

1. Call `list_layers` and show every layer.
2. Call `get_unmapped_layers` and show which layers are unmapped.
3. Call `get_learned_mappings` and show the learned mappings.

Present the result as three short sections: Layers, Unmapped layers, Learned mappings.

Rules:
- Do NOT modify the drawing.
- Do NOT call any other bricsai tool (no apply, learn, explode, rename, delete, lock or raw commands).
- If the bricsai server is not connected, tell the user to check `/mcp` and stop.
