using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class ListLayersToolV19 : IToolPlugin
    {
        public string Name => "List All Layers";
        public string Description => "Extracts and prints a list of all layers currently existing in the drawing in BricsCAD V19.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Give me a list of all layer names'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LIST LAYERS\", \"lisp_code\": \"(command \\\"_.-LAYER\\\" \\\"?\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
