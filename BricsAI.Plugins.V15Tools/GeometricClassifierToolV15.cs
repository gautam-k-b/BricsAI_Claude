using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class GeometricClassifierToolV15 : IToolPlugin
    {
        public string Name => "Geometric Classifiers";
        public string Description => "Identify specific architectural elements without relying on layer names using native C# spatial heuristics.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Migrate booth boxes to Expo_BoothOutline layer'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_BOOTH_BOXES\", \"lisp_code\": \"NET:SELECT_BOOTH_BOXES:Expo_BoothOutline\" }] }\n\n" +
                   "User: 'Select the building lines'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_BUILDING_LINES\", \"lisp_code\": \"NET:SELECT_BUILDING_LINES:Expo_Building\" }] }\n\n" +
                   "User: 'Move columns to Expo_Columns'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_COLUMNS\", \"lisp_code\": \"NET:SELECT_COLUMNS:Expo_Columns\" }] }\n\n" +
                   "User: 'Find utilities'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"NET_SELECT_UTILITIES\", \"lisp_code\": \"NET:SELECT_UTILITIES\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
