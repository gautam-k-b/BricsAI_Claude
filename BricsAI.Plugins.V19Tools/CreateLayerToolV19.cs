using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class CreateLayerToolV19 : IToolPlugin
    {
        public string Name => "Create New Layer";
        public string Description => "Creates a new layer in BricsCAD V19. Note: this uses the 'Make' command which also sets it as the current layer.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            // V19 handles Enter/Escapes slightly differently via COM, ensuring we pass exactly enough Enters
            return "User: 'Create a new layer called Wall'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"CREATE LAYER\", \"lisp_code\": \"(command \\\"_.-LAYER\\\" \\\"Make\\\" \\\"Wall\\\" \\\"\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
