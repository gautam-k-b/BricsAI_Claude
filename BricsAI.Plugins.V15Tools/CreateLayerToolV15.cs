using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class CreateLayerToolV15 : IToolPlugin
    {
        public string Name => "Create New Layer";
        public string Description => "Creates a new layer in BricsCAD V15. Note: this uses the 'Make' command which also sets it as the current layer.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Create a new layer called Wall'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"CREATE LAYER\", \"lisp_code\": \"(command \\\"_.-LAYER\\\" \\\"M\\\" \\\"Wall\\\" \\\"\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
