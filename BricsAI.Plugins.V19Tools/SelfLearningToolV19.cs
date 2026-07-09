using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class SelfLearningToolV19 : IToolPlugin
    {
        public string Name => "Learn Rule";
        public string Description => "Saves a permanent user preference, workflow rule, or instruction into the agent's long-term memory for future sessions.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Always draw circles in red layer.'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LEARN_RULE\", \"lisp_code\": \"NET:LEARN_RULE: Always draw circles in the red layer.\" }] }\n\n" +
                   "User: 'Remember that temporary layers should be deleted before proofing.'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"LEARN_RULE\", \"lisp_code\": \"NET:LEARN_RULE: Temporary layers should be deleted before proofing.\" }] }";
        }

        public bool CanExecute(string netCommandName)
        {
            return netCommandName.StartsWith("NET:LEARN_RULE:");
        }

        public string Execute(dynamic doc, string netCmd)
        {
            var rule = netCmd.Substring("NET:LEARN_RULE:".Length).Trim();
            if (string.IsNullOrWhiteSpace(rule)) return "Error: No rule provided to learn.";
            
            KnowledgeService.SaveLearning(rule);
            
            return $"Successfully learned rule: {rule}";
        }
    }
}
