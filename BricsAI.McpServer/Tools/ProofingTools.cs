using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using BricsAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace BricsAI.McpServer.Tools
{
    [McpServerToolType]
    public static class ProofingTools
    {
        [McpServerTool(Name = "run_full_proofing"), Description(
            "Runs the complete, standard proofing sequence deterministically, in the correct order: " +
            "unlock non-booth layers, lock booth layers, prepare/explode geometry, apply learned layer mappings, " +
            "apply standard colors, purge, and rename any remaining non-standard layers to Deleted_. " +
            "This never deletes anything and never learns new mappings — results depend on mappings already learned via " +
            "learn_layer_mapping, so resolve unmapped layers (get_unmapped_layers / train_layer_mapping) first if needed. " +
            "Prefer this tool over calling the individual steps yourself.")]
        public static async Task<string> RunFullProofing(ComClient comClient, StaComHost sta)
        {
            var log = new StringBuilder();

            await sta.InvokeAsync(() => comClient.ForceUnlockAllLayersExceptBoothLayers());
            log.AppendLine("Step 0a: Unlocked all layers except protected booth layers.");

            log.AppendLine("Step 0b: " + await ToolExec.RunAsync(comClient, sta, "NET:LOCK_BOOTH_LAYERS"));
            log.AppendLine("Step A: " + await ToolExec.RunAsync(comClient, sta, "NET:PREPARE_GEOMETRY"));
            log.AppendLine("Step B: " + await ToolExec.RunAsync(comClient, sta, "NET:APPLY_LAYER_MAPPINGS"));
            log.AppendLine("Step C: " + await ToolExec.RawAsync(comClient, sta, "(c:a2zcolor)"));
            log.AppendLine("Step D1: " + await ToolExec.RawAsync(comClient, sta, "(command \"-PURGE\" \"All\" \"*\" \"N\")"));
            log.AppendLine("Step D2: " + await ToolExec.RunAsync(comClient, sta, "NET:RENAME_DELETED_LAYERS"));

            return log.ToString();
        }

        [McpServerTool(Name = "clean_deleted_layers"), Description(
            "Permanently deletes all layers with the given prefix (default 'Deleted_') and purges the drawing. " +
            "DESTRUCTIVE — only call this on an explicit, separate user request, never as part of routine proofing.")]
        public static async Task<string> CleanDeletedLayers(
            ComClient comClient, StaComHost sta,
            [Description("Layer name prefix to permanently delete.")] string prefix = "Deleted_")
        {
            var log = new StringBuilder();
            log.AppendLine(await ToolExec.RunAsync(comClient, sta, $"NET:DELETE_LAYERS_BY_PREFIX:{prefix}"));
            log.AppendLine(await ToolExec.RawAsync(comClient, sta, "(command \"-PURGE\" \"All\" \"*\" \"N\")"));
            return log.ToString();
        }
    }
}
