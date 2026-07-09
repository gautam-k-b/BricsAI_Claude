using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class PurgeToolV19 : IToolPlugin
    {
        public string Name => "Purge Empty Layers and Blocks";
        public string Description => "Purges all empty layers and unused blocks from the BricsCAD V19 drawing.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Clean up the drawing by removing unused layers'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"PURGE\", \"lisp_code\": \"(command \\\"_.-PURGE\\\" \\\"BA\\\" \\\"*\\\" \\\"N\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
