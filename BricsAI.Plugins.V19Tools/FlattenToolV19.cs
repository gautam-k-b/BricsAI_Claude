using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class FlattenToolV19 : IToolPlugin
    {
        public string Name => "Flatten Drawing";
        public string Description => "Flattens all entities in the BricsCAD V19 drawing to 2D (Z=0).";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Flatten everything in the drawing'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"FLATTEN\", \"lisp_code\": \"(command \\\"_.FLATTEN\\\" (ssget \\\"_X\\\") \\\"\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
