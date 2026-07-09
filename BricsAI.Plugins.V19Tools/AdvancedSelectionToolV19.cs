using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class AdvancedSelectionToolV19 : IToolPlugin
    {
        public string Name => "Advanced Object Selection";
        public string Description => "Selects all objects on a layer silently via the C# COM interface. Teaches the agent to use NET:SELECT_LAYER: instead of the raw SELECT command to avoid command-line prompt issues.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Select all circles on layer 0'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT\", \"lisp_code\": \"NET:SELECT_LAYER: 0\" }] }\n\n" +
                   "User: 'Clear my selection' or 'Deselect everything'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LISP_DESELECT\", \"lisp_code\": \"(sssetfirst nil)\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
