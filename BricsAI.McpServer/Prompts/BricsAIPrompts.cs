using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace BricsAI.McpServer.Prompts
{
    [McpServerPromptType]
    public static class BricsAIPrompts
    {
        [McpServerPrompt(Name = "proof_drawing"), Description("Runs the standard A2Z exhibition-drawing proofing workflow.")]
        public static ChatMessage ProofDrawing() => new(ChatRole.User, @"
Proof this BricsCAD drawing for an exhibition context, following the standard A2Z layering rules.

STANDARD A2Z TARGET LAYERS:
- Expo_BoothOutline    -> Polyline boundaries of each exhibitor booth
- Expo_BoothNumber     -> Text labels with booth numbers
- Expo_MaxBoothOutline -> Oversized / max-footprint booth outlines
- Expo_MaxBoothNumber  -> Text labels for max-footprint booth numbers
- Expo_Building        -> Walls, columns, doors, stairs, railings
- Expo_Column          -> Structural column markers
- Expo_Markings        -> Annotations, dimensions, aisle labels, title blocks
- Expo_View2           -> Viewports, print-layout frames, utilities
- Expo_NES             -> Non-exhibiting spaces (service areas, restrooms)
- Defpoints            -> BricsCAD internal dimension points -- never modify
- 0                    -> BricsCAD default layer -- never modify

EXACT-MATCH RULE: a layer only counts as standard if its name matches one of the above character-for-character,
including case. 'Expo_BoothOutline MAX' is NOT Expo_BoothOutline. Treat anything with extra words, suffixes,
or spacing as non-standard.

BEFORE PROOFING:
1. Call get_unmapped_layers to see which layers still need to be classified.
2. Call get_learned_mappings to see what's already known -- don't re-ask about layers already handled.
3. For any layer that's still unmapped, resolve it first (see the train_layer_mapping prompt for the classification
   approach) so apply_layer_mappings has something to work with. Only call learn_layer_mapping when you or the user
   have deliberately decided on a mapping -- never invent one silently during proofing, and never call it just
   because a layer resembles a standard one.

PROOFING: prefer calling the single run_full_proofing tool -- it performs the correct sequence (unlock, lock booth
layers, prepare geometry, apply mappings, apply colors, purge, rename remaining non-standard layers to Deleted_)
deterministically and safely. Only fall back to the granular tools individually if the user is asking for a
non-standard, partial variant of proofing.

NEVER call delete_layers_by_prefix as part of a normal proofing run -- Deleted_ layers are meant to be reviewed,
not immediately purged. Only delete them on a separate, explicit user request (that's what clean_deleted_layers is for).

If you need to hand-write LISP via send_raw_command, defensively wrap any ssget usage in an if so BricsCAD doesn't
freeze on an empty selection, e.g.:
  (if (setq ss (ssget ""_X"" '((8 . ""LayerName"")))) (command ""_.ERASE"" ss """"))
");

        [McpServerPrompt(Name = "clean_geometry"), Description("Fast cleanup: permanently deletes retired Deleted_ layers and purges the drawing.")]
        public static ChatMessage CleanGeometry() => new(ChatRole.User, @"
Clean up the drawing geometry: permanently delete the retired 'Deleted_' layers left over from a prior proofing run,
and purge the drawing. Prefer calling the clean_deleted_layers tool, which does both in one step. This is destructive
(it permanently removes the Deleted_ layers) so only do it because the user explicitly asked for cleanup, not as a
routine part of proofing.
");

        [McpServerPrompt(Name = "generate_summary"), Description("Read-only audit: summarizes the drawing's layers and layer-mapping state without modifying anything.")]
        public static ChatMessage GenerateSummary() => new(ChatRole.User, @"
Generate a read-only Bill of Materials / audit summary for this drawing. Do not modify the drawing in any way --
use only read-only tools: list_layers, get_unmapped_layers, get_learned_mappings, count_empty_booths, and
poll_layer_semantics / get_layer_geometry if you need more detail on a specific layer. Summarize what standard A2Z layers are present,
what vendor layers remain unmapped, and any notable findings (e.g. booths missing numbers via count_empty_booths).
Do not call apply_layer_mappings, run_full_proofing, learn_layer_mapping, or any other mutating tool.
");

        [McpServerPrompt(Name = "train_layer_mapping"), Description("Classifies unmapped vendor layers into standard A2Z targets and records accepted mappings.")]
        public static ChatMessage TrainLayerMapping() => new(ChatRole.User, @"
Find and classify unmapped vendor layers into the standard A2Z target layers.

1. Call get_unmapped_layers to get the list, and get_learned_mappings for context on what's already known.
2. Classify each layer by NAME first:
   - If the name strongly suggests a target (e.g. 'A-WALL', 'G-COLS', 'BOOTH-NUM', 'E-PWR'), you can be confident.
   - If the name is ambiguous (numeric codes, generic names like 'MISC', 'LAYER1', single letters), it's uncertain --
     you need geometry evidence.
3. For uncertain layers, call poll_layer_semantics(layer) to get an entity-type histogram, block names, and text
        samples. If that is still ambiguous, call get_layer_geometry(layer, offset, maxEntities) and inspect the
        returned entity-level data. Use pagination if needed for dense layers.
        Classify using this evidence:
   - Expo_View2: electrical ports, power drops, building utilities, fire exits/hoses, keep-clear demarcations.
   - Expo_Column: column-like structural supports and pillars.
   - Expo_BoothOutline: booth boundary polylines/rectangles.
   - Expo_BoothNumber: booth number text labels.
   - Expo_Building: walls, partitions, doors, stairs, airwalls, permanent fixtures, railings.
   - Expo_Markings: entrances, washroom labels, show titles, hall names, general non-booth text.
   - Expo_NES: non-exhibiting spaces -- enclosed boxes with text like service areas or restrooms.
4. For each layer, state the proposed mapping and a one-sentence reason in chat BEFORE calling learn_layer_mapping --
   don't call it silently. The user will be asked to approve the call regardless, but the stated reasoning is what
   makes the review meaningful.
5. Never propose mapping a layer to itself, and never re-propose a layer that get_learned_mappings shows is already handled.
");
    }
}
