using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class SetLayerToolV19 : IToolPlugin
    {
        public string Name => "Set Current Layer";
        public string Description => "Sets an existing layer as the active/current layer in BricsCAD V19.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Make layer Doors the current layer'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SET LAYER\", \"lisp_code\": \"(command \\\"_.-LAYER\\\" \\\"Set\\\" \\\"Doors\\\" \\\"\\\")\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
