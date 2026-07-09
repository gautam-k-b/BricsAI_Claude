using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class LayerToolV15 : IToolPlugin
    {
        public string Name => "Open Layer Window";
        public string Description => "Opens the layer window in BricsCAD V15 Classic.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Open layer window'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"EXPLORER\", \"lisp_code\": \"(command \\\"_.EXPLORER\\\" \\\"L\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
