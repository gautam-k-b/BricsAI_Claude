using System.ComponentModel;
using System.Threading.Tasks;
using BricsAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace BricsAI.McpServer.Tools
{
    [McpServerToolType]
    public static class LayerTools
    {
        [McpServerTool(Name = "list_layers"), Description("Returns every layer name in the active BricsCAD drawing, excluding frozen layers.")]
        public static Task<string> ListLayers(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:GET_LAYERS");

        [McpServerTool(Name = "select_layer"), Description("Highlights all objects on a layer, or moves them to targetLayer if given.")]
        public static Task<string> SelectLayer(
            ComClient comClient, StaComHost sta,
            [Description("The source layer name.")] string sourceLayer,
            [Description("Optional destination layer to move matching objects to.")] string? targetLayer = null)
        {
            string cmd = targetLayer == null
                ? $"NET:SELECT_LAYER:{sourceLayer}"
                : $"NET:SELECT_LAYER:{sourceLayer}:{targetLayer}";
            return ToolExec.RunAsync(comClient, sta, cmd);
        }

        [McpServerTool(Name = "select_outer_object"), Description("Selects the single largest-area object on a layer (outer-boundary heuristic).")]
        public static Task<string> SelectOuterObject(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:SELECT_OUTER:{layer}");

        [McpServerTool(Name = "select_inner_objects"), Description("Selects all but the largest-area object on a layer (inner/hole heuristic).")]
        public static Task<string> SelectInnerObjects(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:SELECT_INNER:{layer}");

        [McpServerTool(Name = "apply_layer_mappings"), Description(
            "Moves entities from every learned source layer to its mapped standard A2Z target layer, using agent_knowledge.txt. " +
            "Always locks the booth output layers first so they can never be affected by this pass.")]
        public static async Task<string> ApplyLayerMappings(ComClient comClient, StaComHost sta)
        {
            string lockResult = await ToolExec.RunAsync(comClient, sta, "NET:LOCK_BOOTH_LAYERS");
            string applyResult = await ToolExec.RunAsync(comClient, sta, "NET:APPLY_LAYER_MAPPINGS");
            return $"{lockResult}\n{applyResult}";
        }

        [McpServerTool(Name = "rename_unmapped_layers_to_deleted"), Description(
            "Renames every non-standard, non-allow-listed layer with a 'Deleted_' prefix. Does not delete anything.")]
        public static Task<string> RenameUnmappedLayersToDeleted(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:RENAME_DELETED_LAYERS");

        [McpServerTool(Name = "delete_layers_by_prefix"), Description(
            "DESTRUCTIVE. Permanently empties and deletes every layer whose name starts with the given prefix. " +
            "Only call this on a separate, explicit user request — never as an automatic part of routine proofing.")]
        public static Task<string> DeleteLayersByPrefix(
            ComClient comClient, StaComHost sta,
            [Description("Layer name prefix to delete, e.g. 'Deleted_'.")] string prefix = "Deleted_")
            => ToolExec.RunAsync(comClient, sta, $"NET:DELETE_LAYERS_BY_PREFIX:{prefix}");

        [McpServerTool(Name = "erase_entities_on_layer"), Description("Erases all entities on a layer without deleting the layer itself.")]
        public static Task<string> EraseEntitiesOnLayer(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:ERASE_ENTITIES_ON_LAYER:{layer}");

        [McpServerTool(Name = "unlock_layers_by_prefix"), Description("Unlocks all layers whose names start with the given prefix, except the protected booth output layers.")]
        public static Task<string> UnlockLayersByPrefix(ComClient comClient, StaComHost sta, [Description("Layer name prefix.")] string prefix)
            => ToolExec.RunAsync(comClient, sta, $"NET:UNLOCK_LAYERS_BY_PREFIX:{prefix}");

        [McpServerTool(Name = "lock_booth_layers"), Description("Locks Expo_BoothOutline, Expo_BoothNumber, Expo_MaxBoothOutline, and Expo_MaxBoothNumber to protect them during cleanup. Idempotent.")]
        public static Task<string> LockBoothLayers(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:LOCK_BOOTH_LAYERS");

        [McpServerTool(Name = "poll_layer_semantics"), Description(
            "Returns an entity-type histogram, block names, and text samples for a layer — use this as geometry evidence " +
            "when a layer's name alone isn't enough to classify it into a standard A2Z target.")]
        public static Task<string> PollLayerSemantics(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:POLL_LAYER_SEMANTICS:{layer}");

        [McpServerTool(Name = "get_layer_geometry"), Description(
            "Returns structured JSON for entities on a layer, including type, bounds, text, block names, and simple geometry samples. " +
            "Supports pagination via offset and maxEntities so large layers can be fetched in chunks.")]
        public static Task<string> GetLayerGeometry(
            ComClient comClient, StaComHost sta,
            [Description("Layer name.")] string layer,
            [Description("Zero-based starting entity index for paging.")] int offset = 0,
            [Description("Maximum number of entities to return in this page (1-1000). Default 200.")] int maxEntities = 200)
            => ToolExec.RunAsync(comClient, sta, $"NET:GET_LAYER_GEOMETRY:{layer}|{offset}|{maxEntities}");

        [McpServerTool(Name = "export_layer_snapshot"), Description(
            "Exports a visual snapshot for a requested layer to a temporary image file and returns metadata with the file path. " +
            "Useful for human review when geometry JSON is hard to interpret.")]
        public static Task<string> ExportLayerSnapshot(
            ComClient comClient, StaComHost sta,
            [Description("Layer name.")] string layer,
            [Description("Preferred export format. Common values: BMP, PNG, JPG. Default BMP.")] string format = "BMP")
            => ToolExec.RunAsync(comClient, sta, $"NET:EXPORT_LAYER_SNAPSHOT:{layer}|{format}");

        [McpServerTool(Name = "learn_layer_mapping"), Description(
            "Permanently records a source-layer to standard-target-layer mapping in agent_knowledge.txt. " +
            "Only call this when you (or the user) have deliberately decided on a mapping — state the mapping and your reasoning " +
            "in chat before calling this tool.")]
        public static Task<string> LearnLayerMapping(
            ComClient comClient, StaComHost sta,
            [Description("The non-standard vendor layer name.")] string sourceLayer,
            [Description("The standard A2Z target layer name, e.g. Expo_Building.")] string targetLayer)
            => ToolExec.RunAsync(comClient, sta, $"NET:LEARN_LAYER_MAPPING:{sourceLayer}:{targetLayer}");

        [McpServerTool(Name = "isolate_layers"), Description("Hides all layers except the listed ones (plus layer 0).")]
        public static Task<string> IsolateLayers(
            ComClient comClient, StaComHost sta,
            [Description("Comma-separated list of layer names to keep visible.")] string layers)
            => ToolExec.RunAsync(comClient, sta, $"NET:ISOLATE_LAYERS:{layers}");

        [McpServerTool(Name = "show_layer"), Description("Makes a single layer visible.")]
        public static Task<string> ShowLayer(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:SHOW_LAYER:{layer}");

        [McpServerTool(Name = "hide_layer"), Description("Makes a single layer invisible.")]
        public static Task<string> HideLayer(ComClient comClient, StaComHost sta, [Description("Layer name.")] string layer)
            => ToolExec.RunAsync(comClient, sta, $"NET:HIDE_LAYER:{layer}");

        [McpServerTool(Name = "show_all_layers"), Description("Makes every layer visible.")]
        public static Task<string> ShowAllLayers(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:SHOW_ALL_LAYERS");

        [McpServerTool(Name = "send_raw_command"), Description(
            "Escape hatch: sends a raw LISP or native BricsCAD command line string directly, for things with no dedicated tool " +
            "(e.g. '(c:a2zcolor)' or '(command \"-PURGE\" \"All\" \"*\" \"N\")'). Prefer a dedicated tool whenever one exists. " +
            "When hand-writing LISP that uses ssget inside a command, defensively wrap it in an if so BricsCAD doesn't freeze on an empty selection, " +
            "e.g. (if (setq ss (ssget \"_X\" '((8 . \"LayerName\")))) (command \"_.ERASE\" ss \"\")).")]
        public static Task<string> SendRawCommand(ComClient comClient, StaComHost sta, [Description("The LISP or command-line string to send.")] string command)
            => ToolExec.RawAsync(comClient, sta, command);
    }
}
