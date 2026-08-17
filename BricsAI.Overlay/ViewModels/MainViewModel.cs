using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using BricsAI.Core;
using BricsAI.Overlay.Models;
using BricsAI.Overlay.Services.Agents;

namespace BricsAI.Overlay.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ChatMessage> Messages { get; set; } = new ObservableCollection<ChatMessage>();

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(IsQuickActionsEnabled));
            }
        }

        public bool IsNotBusy => !IsBusy;
        public bool IsQuickActionsEnabled => !IsBusy && !_isInTableMappingReview;

        public ICommand SendCommand { get; }
        
        // Quick Actions Dashboard Commands
        public ICommand RunProofingCommand { get; }
        public ICommand CleanGeometryCommand { get; }
        public ICommand GenerateSummaryCommand { get; }

        private readonly Services.ComClient _comClient; // Replaced PipeClient
        private readonly SurveyorAgent _surveyor;
        private readonly ExecutorAgent _executor;
        private readonly ValidatorAgent _validator;
        private readonly MapperAgent _mapper;
        private readonly MappingReviewAgent _mappingReviewAgent;

        private string _pendingMappingCommands = "";
        private string _originalProofingCommand = "";
        private string _lastKnownMappings = ""; // Persists across failures for context recovery

        // Tabular mapping review state — a single numbered table is shown once, and the user can
        // reply across one or more turns (comma-separated indexes, a memorize instruction, a layer
        // action, a question, "confirm all", or "abort") until every row has been decided.
        private bool _isInTableMappingReview = false;
        private List<(string Source, string Target)> _mappingQueue = new List<(string, string)>();
        private HashSet<int> _includedIndexes = new HashSet<int>();
        private HashSet<int> _excludedIndexes = new HashSet<int>();
        private Dictionary<string, string> _mappingReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _mappingConfidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MainViewModel()
        {
            _comClient = new Services.ComClient();
            _surveyor = new SurveyorAgent();
            _executor = new ExecutorAgent();
            _validator = new ValidatorAgent();
            _mapper = new MapperAgent();
            _mappingReviewAgent = new MappingReviewAgent();

            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            RunProofingCommand = new RelayCommand(async _ => await ExecuteQuickAction("Please proof this drawing for an exhibition context. Follow the standard A2Z layering, exploding, and layout rules."));
            CleanGeometryCommand = new RelayCommand(async _ => await ExecuteQuickAction("Clean up the drawing geometry. Delete floating layers, standard garbage layers (like dim/freeze), and run PURGE on everything."));
            GenerateSummaryCommand = new RelayCommand(async _ => await ExecuteQuickAction("I don't need macros run. Please just look at the Surveyor data and generate a Bill of Materials / Audit Summary for this layout."));
            
            // Initial greeting
            Messages.Add(new ChatMessage { Role = "Assistant", Content = "Hello! I am your BricsCAD AI Agent. connecting via COM Automation... (No NETLOAD needed)" });
        }

        private async Task ExecuteQuickAction(string overridePrompt)
        {
            if (IsBusy) return;
            string originalInput = InputText;
            InputText = overridePrompt;
            await SendMessageAsync();
            InputText = originalInput; // Restore whatever they were typing
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            var userMessage = InputText;
            InputText = ""; // Clear input immediately
            
            Messages.Add(new ChatMessage { Role = "User", Content = userMessage });
            BricsAI.Core.LoggerService.LogUserMessage(userMessage);

            try
            {
                // --- INTERACTIVE TABULAR MAPPING REVIEW INTERCEPTION ---
                if (_isInTableMappingReview && _mappingQueue.Count > 0)
                {
                    IsBusy = true;

                    var (reviewResponse, tokens, inputTokens, outputTokens) = await _mappingReviewAgent.ClassifyTableReviewResponseAsync(
                        userMessage, _mappingQueue, _mappingConfidence, _mappingReasons, _includedIndexes, _excludedIndexes);

                    switch (reviewResponse.Intent)
                    {
                        case "INCLUDE":
                        {
                            var applied = new List<int>();
                            foreach (var idx in reviewResponse.Indexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                {
                                    _includedIndexes.Add(idx);
                                    _excludedIndexes.Remove(idx);
                                    applied.Add(idx);
                                }
                            }
                            Messages.Add(new ChatMessage { Role = "Assistant", Content = applied.Count > 0 ? $"✅ Included row(s): {string.Join(", ", applied)}" : "I couldn't match any valid row numbers in that — please reference the row numbers from the table." });
                            break;
                        }
                        case "EXCLUDE":
                        {
                            var applied = new List<int>();
                            foreach (var idx in reviewResponse.Indexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                {
                                    _excludedIndexes.Add(idx);
                                    _includedIndexes.Remove(idx);
                                    applied.Add(idx);
                                }
                            }
                            Messages.Add(new ChatMessage { Role = "Assistant", Content = applied.Count > 0 ? $"⏭️ Excluded row(s): {string.Join(", ", applied)}" : "I couldn't match any valid row numbers in that — please reference the row numbers from the table." });
                            break;
                        }
                        case "MEMORIZE":
                        {
                            if (!string.IsNullOrWhiteSpace(reviewResponse.RuleText))
                            {
                                BricsAI.Core.KnowledgeService.SaveLearning(reviewResponse.RuleText);
                                Messages.Add(new ChatMessage { Role = "Assistant", Content = $"🧠 Remembered: {reviewResponse.RuleText}\n\n📋 The mapping review is still open — continue deciding the remaining rows." });
                            }
                            break;
                        }
                        case "CONFIRM_ALL":
                        {
                            for (int i = 1; i <= _mappingQueue.Count; i++)
                                if (!_excludedIndexes.Contains(i)) _includedIndexes.Add(i);
                            break;
                        }
                        case "ACTION":
                        {
                            var actionMsg = new ChatMessage { Role = "Assistant", Content = "🎯 Got it! Performing that layer action in BricsCAD...", IsThinking = true };
                            Messages.Add(actionMsg);
                            try
                            {
                                var (actionPlan, _, _, _) = await _mappingReviewAgent.BuildLayerActionPlanAsync(_pendingMappingCommands, userMessage);
                                IProgress<string> actionProgress = new Progress<string>(update => { actionMsg.Content += $"\n{update}"; });
                                string result = await Task.Run(() => _comClient.ExecuteActionAsync(actionPlan, actionProgress));
                                actionMsg.IsThinking = false;
                                actionMsg.Content = $"✅ Done! {result}\n\n📋 The mapping review is still open.";
                            }
                            catch (Exception ex)
                            {
                                actionMsg.IsThinking = false;
                                actionMsg.Content = $"⚠️ Could not execute layer action: {ex.Message}";
                            }
                            break;
                        }
                        case "QUESTION":
                        {
                            var answerMsg = new ChatMessage { Role = "Assistant", Content = "🤔 Let me check the proposed mappings...", IsThinking = true };
                            Messages.Add(answerMsg);
                            try
                            {
                                string answer = await _mappingReviewAgent.AnswerMappingQuestionAsync(_pendingMappingCommands, userMessage);
                                answerMsg.IsThinking = false;
                                answerMsg.Content = $"💬 {answer}\n\n📋 The mapping review is still open.";
                            }
                            catch (Exception ex)
                            {
                                answerMsg.IsThinking = false;
                                answerMsg.Content = $"⚠️ Could not answer question: {ex.Message}";
                            }
                            break;
                        }
                        case "ABORT":
                            AbortMappingReview();
                            IsBusy = false;
                            return;
                    }

                    bool allDecided = _includedIndexes.Count + _excludedIndexes.Count >= _mappingQueue.Count;
                    if (allDecided)
                    {
                        CompleteMappingReview();
                    }
                    else if (reviewResponse.Intent == "INCLUDE" || reviewResponse.Intent == "EXCLUDE")
                    {
                        Messages.Add(new ChatMessage { Role = "Assistant", Content = FormatMappingTable(), IsTableContent = true });
                    }

                    IsBusy = false;
                    return;
                }

            // --- CONTEXT RECOVERY: Restore previous mapping suggestions if the last session failed ---
            // If the user re-prompts proofing and we have stored mappings from a previous failed attempt,
            // skip the expensive Surveyor+Mapper loop and resume straight from the known mappings.
            bool skipMappingReviewEarly = userMessage.Contains("_skipMappingReviewSequence_");
            string cleanUserMessageEarly = userMessage.Replace("_skipMappingReviewSequence_", "").Trim();
            bool isProofingRetry = !skipMappingReviewEarly &&
                                   !string.IsNullOrEmpty(_lastKnownMappings) &&
                                   (cleanUserMessageEarly.Contains("proof", StringComparison.OrdinalIgnoreCase) ||
                                    cleanUserMessageEarly.Contains("map ", StringComparison.OrdinalIgnoreCase) ||
                                    cleanUserMessageEarly.Contains("remap", StringComparison.OrdinalIgnoreCase) ||
                                    cleanUserMessageEarly.Contains("standardize", StringComparison.OrdinalIgnoreCase));

            if (isProofingRetry)
            {
                _pendingMappingCommands = _lastKnownMappings;
                _originalProofingCommand = cleanUserMessageEarly;

                // Resume the tabular mapping review for this session
                _isInTableMappingReview = true;
                _mappingQueue = ExtractMappingPairsFromJson(_lastKnownMappings);
                _includedIndexes.Clear();
                _excludedIndexes.Clear();

                if (_mappingQueue.Count > 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = "Assistant",
                        Content = $"🔁 **Resuming mapping review from previous session**\n\n{FormatMappingTable()}",
                        IsTableContent = true
                    });
                }
                else
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = "Assistant",
                        Content = $"🔁 **Resuming from previous session** — no mapping proposals found. Proceeding with proofing..."
                    });
                    _isInTableMappingReview = false;
                }

                OnPropertyChanged(nameof(IsQuickActionsEnabled));
                IsBusy = false;
                return;
            }

            IsBusy = true;
            
            // 0. Ensure connected to get the version
            if (!_comClient.IsConnected)
            {
                await _comClient.ConnectAsync();
            }

            // --- CLEANUP FASTPATH: bypass Surveyor/mapper entirely for Clean Geometry command ---
            // The full Surveyor cycle polls semantics on every unknown layer (~1s each), causing
            // 80+ seconds of delay for large drawings. Cleanup only needs DELETE + PURGE.
            bool isCleanupCommand = userMessage.Contains("Delete floating layers", StringComparison.OrdinalIgnoreCase) ||
                                    userMessage.Contains("standard garbage layers", StringComparison.OrdinalIgnoreCase);
            if (isCleanupCommand && _comClient.IsConnected)
            {
                var cleanupMsg = new ChatMessage { Role = "Assistant", Content = "🧹 Running cleanup: deleting Deleted_ layers and purging drawing...", IsThinking = true };
                Messages.Add(cleanupMsg);
                IProgress<string> cleanupProgress = new Progress<string>(update => { cleanupMsg.Content += $"\n{update}"; });
                var cleanupStopwatch = Stopwatch.StartNew();

                try
                {
                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Cleanup fastpath — starting DELETE_LAYERS_BY_PREFIX:Deleted_.");
                    string deleteAction = @"{ ""tool_calls"": [{ ""command_name"": ""DELETE_LAYERS_BY_PREFIX"", ""lisp_code"": ""NET:DELETE_LAYERS_BY_PREFIX:Deleted_"" }] }";
                    string deleteResult = await Task.Run(() => _comClient.ExecuteActionAsync(deleteAction, cleanupProgress));

                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Cleanup fastpath — running PURGE.");
                    string purgeAction = @"{ ""tool_calls"": [{ ""command_name"": ""PURGE"", ""lisp_code"": ""(command \""-PURGE\"" \""All\"" \""*\"" \""N\"")""  }] }";
                    string purgeResult = await Task.Run(() => _comClient.ExecuteActionAsync(purgeAction, cleanupProgress));

                    cleanupStopwatch.Stop();
                    double cleanupSeconds = Math.Round(cleanupStopwatch.Elapsed.TotalSeconds, 1);

                    cleanupMsg.IsThinking = false;
                    cleanupMsg.Content = $"✅ Cleanup complete.\n\n{deleteResult}\n{purgeResult}";
                    LoggerService.LogTransaction("PLUGIN", $"MainViewModel: Cleanup fastpath done. {deleteResult}");

                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: 0 API tokens consumed (cleanup runs natively via COM — no AI calls needed). Task completed in {cleanupSeconds} seconds." });
                }
                catch (Exception ex)
                {
                    cleanupMsg.IsThinking = false;
                    cleanupMsg.Content = $"⚠️ Cleanup error: {ex.Message}";
                }

                IsBusy = false;
                return;
            }

            // 1. Globally strip Drafter's physical layer locks BEFORE Surveyor or Executor begins,
            // but preserve final booth output layers that are intentionally locked by workflow.
            if (_comClient.IsConnected)
            {
                LoggerService.LogTransaction("PLUGIN", "MainViewModel: Force unlocking all layers except booth output layers.");
                await Task.Run(() => _comClient.ForceUnlockAllLayersExceptBoothLayersSynchronously());
                LoggerService.LogTransaction("PLUGIN", "MainViewModel: Layer unlock stage complete.");
            }

            // Pass 1: Survey Layers (Two-Pass Logic)
            string currentLayers = "";
            if (_comClient.IsConnected)
            {
                try
                {
                    string getLayersCmd = @"{ ""tool_calls"": [{ ""command_name"": ""NET_GET_LAYERS"", ""lisp_code"": ""NET:GET_LAYERS:"" }] }";
                    currentLayers = await Task.Run(() => _comClient.ExecuteActionAsync(getLayersCmd));
                }
                catch { }
            }

            //Pre-filter known layers before passing to Surveyor
            string cleanLayersForSurveyor = currentLayers;
            if (cleanLayersForSurveyor.Contains("Layers found:"))
                cleanLayersForSurveyor = cleanLayersForSurveyor.Substring(cleanLayersForSurveyor.IndexOf("Layers found:") + "Layers found:".Length);

            var knownMappings = BricsAI.Core.KnowledgeService.GetLayerMappingsDictionary();
            var standardA2zLayersEarly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building",
                "Expo_Markings", "Expo_View2", "Expo_Column", "Expo_NES", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            };

            string layersForSurveyor = string.Join(", ",
                cleanLayersForSurveyor
                    .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l)
                            && !standardA2zLayersEarly.Contains(l)
                            && !knownMappings.ContainsKey(l))
                    .Distinct());


            // --- MULTI-AGENT ORCHESTRATION START ---
            int totalTokens = 0;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;
            var stopwatch = Stopwatch.StartNew();

            // Agent 1: Surveyor
            var surveyorMsg = new ChatMessage { Role = "Assistant", Content = "👷‍♂️ Surveyor Agent: Putting on my hard hat and inspecting the raw drawing layers...", IsThinking = true };
            Messages.Add(surveyorMsg);            
            var surveyorResult = await Task.Run(() => _surveyor.AnalyzeDrawingStateAsync(userMessage, layersForSurveyor));
            surveyorMsg.IsThinking = false;
            string surveyorSummary = surveyorResult.Summary;
            totalTokens += surveyorResult.Tokens;
            totalInputTokens += surveyorResult.InputTokens;
            totalOutputTokens += surveyorResult.OutputTokens;
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📋 Surveyor Report:\n{surveyorSummary}" });

            // Agent 1.5: Semantic Layer Auto-Mapper (Intercept Unknowns via C# deterministic parsing)
            var standardA2zLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building", "Expo_Markings", "Expo_View2", "Expo_Column",
                "Expo_NES", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            };

            // Safely sanitize the COM 'Step' and 'Layers found:' prefix so it doesn't pollute the target logic
            string cleanLayersPayload = currentLayers;
            if (cleanLayersPayload.Contains("Layers found:"))
                cleanLayersPayload = cleanLayersPayload.Substring(cleanLayersPayload.IndexOf("Layers found:") + "Layers found:".Length);

            var unknownLayers = cleanLayersPayload.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !standardA2zLayers.Contains(l))
                .Where(l => 
                {
                    try
                    {
                        var learnings = BricsAI.Core.KnowledgeService.GetLearnings();
                        if (string.IsNullOrWhiteSpace(learnings)) return true;
                        
                        // 1. Direct exact match check inside memory string
                        return !learnings.Contains($"'{l}' to standard layer");
                    }
                    catch
                    {
                        return true;
                    }
                })
                .Distinct()
                .ToList();

            bool skipMappingReview = userMessage.Contains("_skipMappingReviewSequence_");
            string cleanUserMessage = userMessage.Replace("_skipMappingReviewSequence_", "").Trim();
            
            bool isMemoryInstruction = cleanUserMessage.StartsWith("remember", StringComparison.OrdinalIgnoreCase) ||
                                       cleanUserMessage.StartsWith("learn", StringComparison.OrdinalIgnoreCase) ||
                                       cleanUserMessage.StartsWith("forget", StringComparison.OrdinalIgnoreCase);

            // Trigger the mapper whenever unknown layers exist and the command is not a pure memory
            // instruction. The old keyword-gating (proof/map/remap) is removed because the new
            // 2-phase approach is fast (~36s) and users expect proposals for any command that
            // surfaces unknown layers (including Generate Summary).
            if (unknownLayers.Any() && !skipMappingReview && !isMemoryInstruction)
            {
                var mapperMsg = new ChatMessage { Role = "Assistant", Content = $"✨ Mapper Agent: Intercepting {unknownLayers.Count} unknown vendor layers...", IsThinking = true };
                Messages.Add(mapperMsg);
                IProgress<string> mapProgress = new Progress<string>(update => { mapperMsg.Content += $"\n{update}"; });

                var allMappings = new List<BricsAI.Overlay.Services.Agents.MappingResult>();

                // === PHASE 1: Classify by layer name alone (1 LLM call, no COM) ===
                mapProgress.Report($"\n🏷️ Phase 1: Classifying {unknownLayers.Count} layers by name...");
                var phase1 = await _mapper.ClassifyByNameAsync(unknownLayers);
                totalTokens += phase1.Tokens;
                totalInputTokens += phase1.InputTokens;
                totalOutputTokens += phase1.OutputTokens;

                allMappings.AddRange(phase1.Confident);
                mapProgress.Report($"✅ {phase1.Confident.Count} layers classified by name. {phase1.Uncertain.Count} need geometry evidence.");

                // === PHASE 2: Poll geometry for uncertain layers, then batch classify (1 more LLM call) ===
                if (phase1.Uncertain.Any())
                {
                    mapProgress.Report($"\n🔎 Phase 2: Polling geometry for {phase1.Uncertain.Count} uncertain layers...");
                    var footprints = new List<(string LayerName, string Footprint)>();

                    foreach (var unknownLayer in phase1.Uncertain)
                    {
                        mapProgress.Report($"\n   Polling '{unknownLayer}'...");
                        string safeLayerName = unknownLayer.Replace("\"", "\\\"").Replace("\\", "\\\\");
                        string footprintPlan = $@"{{ ""tool_calls"": [{{ ""command_name"": ""POLL_SEMANTICS"", ""lisp_code"": ""NET:POLL_LAYER_SEMANTICS:{safeLayerName}"" }}] }}";
                        string footprint = await Task.Run(() => _comClient.ExecuteActionAsync(footprintPlan, mapProgress));

                        if (!footprint.Contains("Error") && footprint.Length > 10)
                            footprints.Add((unknownLayer, footprint));
                        else
                            mapProgress.Report($"   ⚠️ Empty or unreadable — skipping.");
                    }

                    if (footprints.Any())
                    {
                        mapProgress.Report($"\n🧠 Batch-classifying {footprints.Count} layers by geometry (1 LLM call)...");
                        var phase2 = await _mapper.BatchDeduceByGeometryAsync(footprints);
                        totalTokens += phase2.Tokens;
                        totalInputTokens += phase2.InputTokens;
                        totalOutputTokens += phase2.OutputTokens;
                        allMappings.AddRange(phase2.Mappings);
                        mapProgress.Report($"✅ {phase2.Mappings.Count} additional layers classified by geometry.");
                    }
                }

                mapperMsg.IsThinking = false;

                // Build the pending tool calls JSON from all results
                var pendingToolCalls = allMappings
                    .Select(m => $@"{{ ""command_name"": ""Semantic Mapping"", ""lisp_code"": ""{m.LispCode}"" }}")
                    .ToList();

                // Store reasons and confidence keyed by source layer
                foreach (var m in allMappings)
                {
                    if (!string.IsNullOrEmpty(m.Reason))
                        _mappingReasons[m.SourceLayer] = m.Reason;
                    _mappingConfidence[m.SourceLayer] = m.Confidence;
                }

                if (pendingToolCalls.Any())
                {
                    _pendingMappingCommands = "{ \"tool_calls\": [\n" + string.Join(",\n", pendingToolCalls) + "\n] }";
                    _lastKnownMappings = _pendingMappingCommands; // Persist for context recovery on failure
                    _originalProofingCommand = userMessage;

                    // Initialize the tabular mapping review
                    _isInTableMappingReview = true;
                    _mappingQueue = ExtractMappingPairsFromJson(_pendingMappingCommands);
                    _includedIndexes.Clear();
                    _excludedIndexes.Clear();

                    if (_mappingQueue.Count > 0)
                    {
                        stopwatch.Stop();
                        double surveySeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
                        Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: {totalTokens} API tokens consumed ({totalInputTokens} Input, {totalOutputTokens} Output) mapping {unknownLayers.Count} layers. Surveyor completed in {surveySeconds} seconds." });

                        Messages.Add(new ChatMessage
                        {
                            Role = "Assistant",
                            Content = $"🛑 **Human Review Required** — {_mappingQueue.Count} unknown layer(s) need mapping\n\n{FormatMappingTable()}",
                            IsTableContent = true
                        });
                    }

                    OnPropertyChanged(nameof(IsQuickActionsEnabled));
                    IsBusy = false; // Unlock UI to allow user feedback
                    return; // Halt execution and wait for human response
                }

                // Dictionary file is removed in favor of native KnowledgeService rule memory.
            }

            int maxRetries = 2;
            int attempt = 0;
            bool success = false;
            string feedback = "";

            while (attempt < maxRetries && !success)
            {
                attempt++;
                string executorContext = attempt == 1 ? surveyorSummary : surveyorSummary + $"\n\nVALIDATOR FEEDBACK FROM PREVIOUS ATTEMPT:\n{feedback}";
                
                // Agent 2: Executor
                var executorMsg = new ChatMessage { Role = "Assistant", Content = $"⚙️ Executor Agent: Drafting the master execution plan to restructure your booths! (Attempt {attempt})...", IsThinking = true };
                Messages.Add(executorMsg);
                var executorResult = await Task.Run(() => _executor.GenerateMacrosAsync(cleanUserMessage, executorContext, _comClient.MajorVersion));
                executorMsg.IsThinking = false;
                string actionPlanJson = executorResult.ActionPlan;
                totalTokens += executorResult.Tokens;
                totalInputTokens += executorResult.InputTokens;
                totalOutputTokens += executorResult.OutputTokens;

                // --- SHORT-CIRCUIT: Handle empty tool_calls OR NET:MESSAGE-only plans ---
                bool hasToolCalls = false;
                bool isMessageOnly = false;
                var inlineMessages = new System.Collections.Generic.List<string>();
                try
                {
                    using var planDoc = System.Text.Json.JsonDocument.Parse(actionPlanJson);
                    if (planDoc.RootElement.TryGetProperty("tool_calls", out var tc) && tc.GetArrayLength() > 0)
                    {
                        hasToolCalls = true;
                        // Check if every single tool call is a NET:MESSAGE:
                        bool allMessages = true;
                        foreach (var call in tc.EnumerateArray())
                        {
                            string? lisp = call.TryGetProperty("lisp_code", out var lp) ? lp.GetString() : null;
                            if (lisp != null && lisp.StartsWith("NET:MESSAGE:"))
                            {
                                inlineMessages.Add(lisp.Substring("NET:MESSAGE:".Length).Trim());
                            }
                            else 
                            { 
                                allMessages = false; 
                            }
                        }
                        isMessageOnly = allMessages && inlineMessages.Any();
                    }
                }
                catch { }

                if (!hasToolCalls)
                {
                    // Surveyor already displayed its summary above — just acknowledge and stop.
                    executorMsg.Content = "💬 This is an informational request — no BricsCAD commands needed.";
                    success = true;
                    break;
                }

                if (isMessageOnly)
                {
                    // Pure informational response — show directly in chat, skip BricsCAD execution entirely.
                    executorMsg.Content = "💬 " + string.Join("\n\n", inlineMessages);
                    success = true;
                    break;
                }

                // Execute against COM
                var cadMsg = new ChatMessage { Role = "Assistant", Content = $"🚀 BricsCAD: Hijacking your mouse to execute native tools...", IsThinking = true };
                Messages.Add(cadMsg);

                var progress = new System.Progress<string>(update =>
                {
                    cadMsg.Content += $"\n{update}";
                });

                string executionLogs = await Task.Run(() => _comClient.ExecuteActionAsync(actionPlanJson, progress));
                cadMsg.IsThinking = false;

                // DUMP TO DISK FOR DEBUGGING
                File.WriteAllText("AI_Context.txt", executorContext);
                File.WriteAllText("AI_RawActionPlan.json", actionPlanJson);
                File.WriteAllText("AI_ExecutionLogs.txt", executionLogs);

                // Agent 3: Validator
                var validatorMsg = new ChatMessage { Role = "Assistant", Content = "🔍 Validator Agent: Grabbing my magnifying glass to check BricsCAD's work...", IsThinking = true };
                Messages.Add(validatorMsg);
                var validationResult = await Task.Run(() => _validator.ValidateExecutionAsync(userMessage, executionLogs));
                validatorMsg.IsThinking = false;
                
                success = validationResult.success;
                feedback = validationResult.feedback;
                totalTokens += validationResult.tokens;
                totalInputTokens += validationResult.inputTokens;
                totalOutputTokens += validationResult.outputTokens;

                if (success)
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"✅ Validation Passed: The blueprints look pristine! ({feedback})" });
                }
                else
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"❌ Validation Failed: Hmm, something mathematically doesn't add up... ({feedback})" });
                }
            }

            if (!success)
            {
                // Keep _lastKnownMappings intact so the user can resume from context on the next turn.
                Messages.Add(new ChatMessage { Role = "Assistant", Content = "⚠️ System: Multi-Agent flow exhausted retries. Please refine your layer mappings or manually intervene.\n\n💡 Tip: If you'd like to retry with the previously suggested mappings, just send your proofing request again — I'll remember them." });
            }

            stopwatch.Stop();
            double seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: {totalTokens} API tokens consumed ({totalInputTokens} Input, {totalOutputTokens} Output). Task completed in {seconds} seconds." });

            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_debug_log.txt");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (var msg in Messages) sb.AppendLine($"[{msg.Role}]: {msg.Content}");
                System.IO.File.WriteAllText(logPath, sb.ToString());
            }
            catch { }

            IsBusy = false;
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"❌ A critical system error occurred during orchestration:\n{ex.Message}" });
            IsBusy = false;
        }
    }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void CompleteMappingReview()
        {
            int acceptedCount = 0;
            for (int i = 1; i <= _mappingQueue.Count; i++)
            {
                if (_includedIndexes.Contains(i))
                {
                    var (source, target) = _mappingQueue[i - 1];
                    BricsAI.Core.KnowledgeService.SaveLearning($"Map the layer '{source}' to standard layer '{target}'.");
                    acceptedCount++;
                }
            }
            int skippedCount = _mappingQueue.Count - acceptedCount;

            _isInTableMappingReview = false;
            _mappingQueue.Clear();
            _mappingReasons.Clear();
            _mappingConfidence.Clear();
            _includedIndexes.Clear();
            _excludedIndexes.Clear();
            OnPropertyChanged(nameof(IsQuickActionsEnabled));

            Messages.Add(new ChatMessage
            {
                Role = "Assistant",
                Content = $"✨ Mapping review complete!\n\n📊 Summary:\n• **Included:** {acceptedCount} mappings\n• **Excluded:** {skippedCount} mappings\n\n⏭️ Proceeding with proofing..."
            });

            _pendingMappingCommands = "";
            IsBusy = false;

            // Resume original proofing command, but skip the mapping review sequence to prevent infinite loop
            _ = ExecuteQuickAction(_originalProofingCommand + " _skipMappingReviewSequence_");
        }

        private void AbortMappingReview()
        {
            _isInTableMappingReview = false;
            _mappingQueue.Clear();
            _mappingReasons.Clear();
            _mappingConfidence.Clear();
            _includedIndexes.Clear();
            _excludedIndexes.Clear();
            OnPropertyChanged(nameof(IsQuickActionsEnabled));
            _pendingMappingCommands = "";
            _originalProofingCommand = "";
            _lastKnownMappings = "";

            Messages.Add(new ChatMessage
            {
                Role = "Assistant",
                Content = "🛑 **Mapping review cancelled by user.** All proposed mappings were discarded. Dashboard unlocked. Your next proofing request will start a fresh scan."
            });

            IsBusy = false;
        }

        /// <summary>
        /// Renders the current mapping queue as a fixed-width, monospace-friendly table
        /// (rendered via ChatMessage.IsTableContent) with per-row confidence, the Mapper's
        /// reasoning for the suggestion, and decision status.
        /// </summary>
        private string FormatMappingTable()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("#   Source Layer                  Target Layer     Confidence  Reason                               Status");
            sb.AppendLine("--  -----------------------------  ---------------  ----------  -----------------------------------  --------");
            for (int i = 0; i < _mappingQueue.Count; i++)
            {
                int idx = i + 1;
                var (source, target) = _mappingQueue[i];
                string confidence = _mappingConfidence.TryGetValue(source, out var c) ? c : "Low";
                string reason = _mappingReasons.TryGetValue(source, out var r) && !string.IsNullOrWhiteSpace(r) ? r : "—";
                string status = _includedIndexes.Contains(idx) ? "included" : _excludedIndexes.Contains(idx) ? "excluded" : "pending";
                sb.AppendLine($"{idx,-3} {Truncate(source, 29),-29}  {Truncate(target, 15),-15}  {confidence,-10}  {Truncate(reason, 37),-37}  {status}");
            }
            sb.Append("\nReply with row numbers to include/exclude (e.g. \"include 1,3,5\" or \"exclude 2,4\"), say \"confirm all\" to accept everything pending, \"cancel\" to abort, or tell me a rule to remember at any point.");
            return sb.ToString();
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        private List<(string Source, string Target)> ExtractMappingPairsFromJson(string jsonMappings)
        {
            var pairs = new List<(string, string)>();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonMappings);
                var calls = doc.RootElement.GetProperty("tool_calls");
                foreach (var call in calls.EnumerateArray())
                {
                    string lispCode = call.GetProperty("lisp_code").GetString() ?? "";
                    if (lispCode.StartsWith("NET:LEARN_LAYER_MAPPING:"))
                    {
                        var parts = lispCode.Substring("NET:LEARN_LAYER_MAPPING:".Length).Split(':');
                        if (parts.Length == 2)
                        {
                            pairs.Add((parts[0].Trim(), parts[1].Trim()));
                        }
                    }
                }
            }
            catch { }
            return pairs;
        }

        private string BuildFootprintReason(string footprintJson)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(footprintJson);
                var root = doc.RootElement;
                var parts = new List<string>();

                if (root.TryGetProperty("TotalCount", out var tc))
                    parts.Add($"{tc.GetInt32()} entities total");

                if (root.TryGetProperty("EntityTypes", out var et))
                {
                    foreach (var prop in et.EnumerateObject())
                    {
                        // Strip AcDb prefix for readability: AcDbLine → Line
                        string shortName = prop.Name.Replace("AcDb", "").Replace("Acad", "");
                        parts.Add($"{prop.Value.GetInt32()} {shortName}");
                    }
                }

                if (root.TryGetProperty("BlockNames", out var bn) && bn.GetArrayLength() > 0)
                {
                    var blocks = new List<string>();
                    foreach (var b in bn.EnumerateArray()) blocks.Add(b.GetString() ?? "");
                    parts.Add($"blocks: {string.Join(", ", blocks.Take(3))}");
                }

                if (root.TryGetProperty("TextSample", out var ts) && ts.GetArrayLength() > 0)
                {
                    var texts = new List<string>();
                    foreach (var t in ts.EnumerateArray()) texts.Add($"'{t.GetString()}'");
                    parts.Add($"text: {string.Join(", ", texts.Take(3))}");
                }

                return string.Join(" | ", parts);
            }
            catch
            {
                return "";
            }
        }

        private string FormatMappingsForDisplay(string jsonMappings)
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(jsonMappings);
                var calls = doc.RootElement.GetProperty("tool_calls");
                var formattedMappings = new List<string>();
                foreach (var call in calls.EnumerateArray())
                {
                    string lispCode = call.GetProperty("lisp_code").GetString() ?? "";
                    if (lispCode.StartsWith("NET:LEARN_LAYER_MAPPING:"))
                    {
                        var parts = lispCode.Substring("NET:LEARN_LAYER_MAPPING:".Length).Split(':');
                        if (parts.Length == 2)
                        {
                            formattedMappings.Add($"• **{parts[0]}**  ➔  **{parts[1]}**");
                        }
                    }
                }
                return string.Join("\n", formattedMappings);
            }
            catch
            {
                return jsonMappings; // Fallback to raw JSON if parse fails
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly System.Func<object?, Task> _execute;
        private readonly System.Predicate<object?>? _canExecute;

        public RelayCommand(System.Func<object?, Task> execute, System.Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event System.EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
