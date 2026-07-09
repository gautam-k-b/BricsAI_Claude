using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class CustomLispToolsV19 : IToolPlugin
    {
        public string Name => "Execute Custom a2z LISP Commands";
        public string Description => "Executes one of the 19 custom LISP commands provided in the Expo/a2z library (e.g., A2ZLAYERS, A2ZCOLOR, BOOTHNUMBERS).";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Run the custom a2z color command'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"A2ZCOLOR\", \"lisp_code\": \"(c:a2zcolor)\" }] }\n\n" +
                   "User: 'Create booth numbers'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"BOOTHNUMBERS\", \"lisp_code\": \"(c:boothnumbers)\" }] }\n\n" +
                   "User: 'Select entities on a layer'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELLAY\", \"lisp_code\": \"(c:sellay)\" }] }";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
