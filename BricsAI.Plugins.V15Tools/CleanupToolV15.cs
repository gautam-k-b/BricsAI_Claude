using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class CleanupToolV15 : IToolPlugin
    {
        public string Name => "Cleanup Drawing";
        public string Description => "Purges and audits the drawing in BricsCAD V15 Classic.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Clean up the drawing'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"CLEANUP\", \"lisp_code\": \"(command \\\"_.PURGE\\\" \\\"A\\\" \\\"\\\" \\\"N\\\") (command \\\"_.AUDIT\\\" \\\"Y\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
