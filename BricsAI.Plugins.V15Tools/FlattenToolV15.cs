using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class FlattenToolV15 : IToolPlugin
    {
        public string Name => "Flatten Drawing";
        public string Description => "Flattens all entities in the BricsCAD V15 drawing to 2D (Z=0).";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Flatten everything in the drawing'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"FLATTEN\", \"lisp_code\": \"(command \\\"_.FLATTEN\\\" (ssget \\\"_X\\\") \\\"\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
