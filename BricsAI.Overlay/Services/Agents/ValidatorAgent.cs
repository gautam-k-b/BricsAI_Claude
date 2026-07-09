using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    public class ValidatorAgent : BaseAgent
    {
        public ValidatorAgent()
        {
            Name = "ValidatorAgent";
        }

        public async Task<(bool success, string feedback, int tokens, int inputTokens, int outputTokens)> ValidateExecutionAsync(string userPrompt, string executionLogs)
        {
            string systemPrompt = @"You are the Validator Agent for BricsCAD.
Your job is to read the execution logs resulting from the Executor Agent's actions and determine if the user's objective was met successfully, or if there were errors or missing steps.

If the output log contains exceptions, 'Error', or obvious failures related to the user's objective, you must fail the validation.
If the steps executed log actual commands (e.g., `[NET:SELECT...]`, `[(command ""_.EXPLODE""...)]`, `[(command ""-LAYER"" ""LOCK""...)]`) that align with the user's proofing request, pass it. Do not fail just because a step was skipped if the overall intent was achieved.

OUTPUT FORMAT:
The very first word of your response MUST BE exactly 'PASS' or 'FAIL'.
Follow that with a newline, and then provide a brief sentence explaining why, or giving feedback to the Executor for the next retry.";

            string prompt = $"USER OBJECTIVE:\n{userPrompt}\n\nEXECUTION LOGS:\n{executionLogs}\n\nDid the execution succeed?";
            
            var result = await CallModelAsync(systemPrompt, prompt, expectJson: false);
            string response = result.Content;
            int tokens = result.TotalTokens;
            int inputTokens = result.InputTokens;
            int outputTokens = result.OutputTokens;
            
            bool success = response.Trim().StartsWith("PASS");
            
            // Extract everything after the first word as feedback
            string feedback = response;
            int spaceIndex = response.IndexOf('\n');
            if (spaceIndex > 0 && spaceIndex < response.Length - 1)
            {
                feedback = response.Substring(spaceIndex + 1).Trim();
            }

            return (success, feedback, tokens, inputTokens, outputTokens);
        }
    }
}
