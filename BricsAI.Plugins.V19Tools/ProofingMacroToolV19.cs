using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class ProofingMacroToolV19 : IToolPlugin
    {
        public string Name => "Proofing Automation Macro";
        public string Description => "Teaches the AI how to string together multiple commands into a massive JSON array to automate the 10-step PSS Proofing process.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Run the proofing process on this file'\n" +
                   "Response: {\n" +
                   "  \"tool_calls\": [\n" +
                   "    { \"command_name\": \"LAYON\", \"lisp_code\": \"(command \\\"LAYON\\\")\" },\n" +
                   "    { \"command_name\": \"EXPLODE_BLOCKS\", \"lisp_code\": \"(command \\\"_.EXPLODE\\\" (ssget \\\"_X\\\" '((0 . \\\"INSERT\\\"))) \\\"\\\")\" },\n" +
                   "    { \"command_name\": \"EXPLODE_BLOCKS_DEEP\", \"lisp_code\": \"(command \\\"_.EXPLODE\\\" (ssget \\\"_X\\\" '((0 . \\\"INSERT\\\"))) \\\"\\\")\" },\n" +
                   "    { \"command_name\": \"FLATTEN_ALL\", \"lisp_code\": \"(command \\\"FLATTEN\\\" (ssget \\\"_X\\\") \\\"\\\")\" },\n" +
                   "    { \"command_name\": \"PURGE_ALL\", \"lisp_code\": \"(command \\\"-PURGE\\\" \\\"All\\\" \\\"*\\\" \\\"N\\\")\" },\n" +
                   "    { \"command_name\": \"A2ZLAYERS\", \"lisp_code\": \"(c:a2zLayers)\" },\n" +
                   "    { \"command_name\": \"A2ZLAYOUTS\", \"lisp_code\": \"(c:a2zLayouts)\" },\n" +
                   "    { \"command_name\": \"A2ZCOLOR\", \"lisp_code\": \"(c:a2zcolor)\" },\n" +
                   "    { \"command_name\": \"GET_LAYERS\", \"lisp_code\": \"NET:GET_LAYERS:\" },\n" +
                   "    { \"command_name\": \"ASK_USER\", \"lisp_code\": \"NET:MESSAGE: I have completed the standard proofing. Please tell me which non-standard layers should be mapped to Expo_Building or Expo_Hall.\" }\n" +
                   "  ]\n" +
                   "}";
        }
        public bool CanExecute(string netCommandName) => false;
        public string Execute(dynamic doc, string netCmd) => "Not implemented natively.";
    }
}
