using System.Text.Json;
using System.Threading.Tasks;

namespace BricsAI.Overlay.Services.Agents
{
    /// <summary>
    /// Gatekeeper that runs before any drawing work. Decides whether a typed message is an actual
    /// CAD/proofing request, or conversation (greeting, small talk, unrelated question) that should be
    /// answered in chat without touching BricsCAD.
    /// </summary>
    public class IntentAgent : BaseAgent
    {
        public IntentAgent() { Name = "IntentAgent"; }

        public const string Chat = "CHAT";
        public const string CadTask = "CAD_TASK";
        public const string FullProofing = "FULL_PROOFING";

        public async Task<(string Intent, string Reply)> ClassifyAsync(string userMessage, string recentContext)
        {
            string systemPrompt = @"You are the front-desk assistant of the BricsAI Overlay, an application whose agents ONLY process BricsCAD drawings for exhibition floor plans (A2Z layer proofing: mapping vendor layers to standard layers, exploding geometry, cleaning/purging layers, booth outlines and numbers, Bill-of-Materials / audit summaries, remembering layer rules).

Classify the user's latest message into exactly one of:
FULL_PROOFING - the user clearly and affirmatively asks to run the complete A2Z proofing / standardization of the whole drawing now (e.g. 'proof this drawing', 'run full proofing', 'standardize the layers'). A negation, postponement, hypothetical or question about proofing is NOT FULL_PROOFING: 'don't proof this yet', 'do not proof', 'not now', 'what does proofing do?', 'should I proof?' are CHAT (acknowledge or answer briefly, and do nothing).
CAD_TASK - the user wants a narrower thing done with, or wants information about, the open drawing (map layers, explode, clean, purge, delete layers, summarize/audit/BOM, list layers, remember/forget a layer rule, follow-ups like 'yes do it' or 'also the booth layers' that clearly continue such work).
CHAT - anything else: greetings (hi, hello, thanks), questions about what you can do or how to use the app, small talk, and general or unrelated questions (weather, coding, trivia, writing help, etc.).

If unsure whether it refers to the drawing, choose CHAT. Never choose CAD_TASK for a bare greeting.

For CHAT write a short, friendly reply (max 4 sentences):
- Greeting/thanks: respond naturally and briefly explain what you can help with (proofing exhibition drawings, mapping vendor layers to A2Z standard layers, exploding geometry, cleaning layers, drawing summaries).
- Question about your capabilities: answer from the description above.
- Unrelated question: say politely that you are specialised for BricsCAD drawing processing only, so you cannot help with that topic, and suggest what you can do instead. Do not answer the unrelated question.
Do not claim to have run anything on the drawing. Do not use markdown tables.

OUTPUT strict JSON only: { ""intent"": ""FULL_PROOFING|CAD_TASK|CHAT"", ""reply"": ""<text for CHAT, empty otherwise>"" }";

            string prompt = (string.IsNullOrWhiteSpace(recentContext) ? "" : $"RECENT CONVERSATION:\n{recentContext}\n\n") +
                            $"LATEST USER MESSAGE:\n{userMessage}";

            var result = await CallModelAsync(systemPrompt, prompt, expectJson: true);
            BricsAI.Core.LoggerService.LogAgentPrompt("IntentGate", result.Content);

            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                string intent = doc.RootElement.TryGetProperty("intent", out var i) ? (i.GetString() ?? "") : "";
                string reply = doc.RootElement.TryGetProperty("reply", out var r) ? (r.GetString() ?? "") : "";
                intent = intent.Trim().ToUpperInvariant();
                if (intent == FullProofing) return (FullProofing, "");
                if (intent == CadTask) return (CadTask, "");
                if (!string.IsNullOrWhiteSpace(reply)) return (Chat, reply);
            }
            catch { }

            // Unparseable answer: do NOT start drawing work on a guess.
            return (Chat, "I'm not sure what you'd like me to do. I can proof exhibition drawings, map vendor layers to the A2Z standard layers, explode geometry, clean up layers and summarise the drawing. What would you like to do?");
        }
    }
}
