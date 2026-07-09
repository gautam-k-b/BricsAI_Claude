using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class LayerToolV19 : IToolPlugin
    {
        public string Name => "Open Layer Window";
        public string Description => "Opens the advanced layer panel in BricsCAD V19+.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Open layer window'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LAYERSPANELOPEN\", \"lisp_code\": \"_LAYERSPANELOPEN\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
