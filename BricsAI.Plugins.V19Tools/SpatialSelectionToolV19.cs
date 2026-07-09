using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class SpatialSelectionToolV19 : IToolPlugin
    {
        public string Name => "Spatial/Geometric Selection (Inner/Outer)";
        public string Description => "Teaches the agent how to select objects based on bounding box size (e.g. inner vs outer). Provides LISP templates for calculating areas of entities in a selection set to isolate the largest or smallest ones.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Select the outer box in layer outlines'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_OUTER\", \"lisp_code\": \"NET:SELECT_OUTER: outlines\" }] }\n\n" +
                   "User: 'Select the inner boxes on layer borders'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_INNER\", \"lisp_code\": \"NET:SELECT_INNER: borders\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
