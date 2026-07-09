using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class LayOnToolV15 : IToolPlugin
    {
        public string Name => "Turn On All Layers";
        public string Description => "Turns on all layers (turns off the lightbulb) in the BricsCAD V15 drawing.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Turn on all layers'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LAYON\", \"lisp_code\": \"(command \\\"_.LAYON\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
