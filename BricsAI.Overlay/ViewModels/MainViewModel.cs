using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
        public ICommand ExplodeGeometryCommand { get; }

        // Mapping-review table buttons
        public ICommand ApplySelectionCommand { get; }
        public ICommand CancelReviewCommand { get; }

        // Layer snapshot popup
        public ICommand ShowLayerSnapshotCommand { get; }
        public ICommand ClosePopupCommand { get; }

        private bool _isPopupVisible;
        public bool IsPopupVisible { get => _isPopupVisible; set { _isPopupVisible = value; OnPropertyChanged(); } }

        private bool _isPopupLoading;
        public bool IsPopupLoading { get => _isPopupLoading; set { _isPopupLoading = value; OnPropertyChanged(); } }

        private string _popupLayerName = "";
        public string PopupLayerName { get => _popupLayerName; set { _popupLayerName = value; OnPropertyChanged(); } }

        private BitmapImage? _popupImage;
        public BitmapImage? PopupImage { get => _popupImage; set { _popupImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPopupImage)); } }
        public bool HasPopupImage => _popupImage != null;

        private string? _popupStatusText;
        public string? PopupStatusText { get => _popupStatusText; set { _popupStatusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPopupStatusText)); } }
        public bool HasPopupStatusText => !string.IsNullOrEmpty(_popupStatusText);

        private readonly Services.ComClient _comClient; // Replaced PipeClient
        private readonly SurveyorAgent _surveyor;
        private readonly ExecutorAgent _executor;
        private readonly ValidatorAgent _validator;
        private readonly MapperAgent _mapper;
        private readonly MappingReviewAgent _mappingReviewAgent;
        private readonly IntentAgent _intentAgent = new IntentAgent();
        private bool _isQuickActionRun = false;
        private const string ProofingButtonPrompt = "Please proof this drawing for an exhibition context. Follow the standard A2Z layering, exploding, and layout rules.";

        private string _lastActiveDocumentPath = "";
        private bool _geometryAlreadyPrepared = false; // true once pre-survey explode ran for this session

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
        private Dictionary<string, string> _mappingSnapshotPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MainViewModel()
        {
            _comClient = new Services.ComClient();
            _surveyor = new SurveyorAgent();
            _executor = new ExecutorAgent();
            _validator = new ValidatorAgent();
            _mapper = new MapperAgent();
            _mappingReviewAgent = new MappingReviewAgent();

            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            RunProofingCommand = new RelayCommand(async _ => await ExecuteQuickAction(ProofingButtonPrompt));
            CleanGeometryCommand = new RelayCommand(async _ => await ExecuteQuickAction("Clean up the drawing geometry. Delete floating layers, standard garbage layers (like dim/freeze), and run PURGE on everything."));
            GenerateSummaryCommand = new RelayCommand(async _ => await ExecuteQuickAction("I don't need macros run. Please just look at the Surveyor data and generate a Bill of Materials / Audit Summary for this layout."));
            ExplodeGeometryCommand = new RelayCommand(async _ => await ExecuteQuickAction("__EXPLODE_WITH_BOOTH_LOCK__ Unlock all layers, lock booth output layers, then iteratively explode all complex entities."));

            ApplySelectionCommand = new RelayCommand(_ => { ApplyTableSelection(); return Task.CompletedTask; });
            CancelReviewCommand = new RelayCommand(_ =>
            {
                if (!IsBusy && _isInTableMappingReview) AbortMappingReview();
                return Task.CompletedTask;
            });

            ClosePopupCommand = new RelayCommand(_ =>
            {
                IsPopupVisible = false;
                PopupImage = null;
                PopupStatusText = null;
                IsPopupLoading = false;
                return Task.CompletedTask;
            });
            ShowLayerSnapshotCommand = new RelayCommand(async param =>
            {
                if (param is MappingRow row) await ShowLayerSnapshotAsync(row);
            });
            
            // Initial greeting
            Messages.Add(new ChatMessage { Role = "Assistant", Content = "Hello! I am your BricsCAD AI Agent. connecting via COM Automation... (No NETLOAD needed)" });

            _ = CheckLlmConnectionAsync();
        }

        /// <summary>Startup check so a bad/missing API key is reported immediately, not silently ignored.</summary>
        private async Task CheckLlmConnectionAsync()
        {
            string? problem = await Task.Run(() => Services.AnthropicRuntime.CheckConnectivityAsync());
            Messages.Add(new ChatMessage
            {
                Role = "Assistant",
                Content = problem == null
                    ? "✅ Connected to the AI service."
                    : $"🔌 **AI service not available — I can't process drawings until this is fixed.**\n{problem}"
            });
            LoggerService.LogTransaction("OVERLAY", problem == null ? "LLM connectivity OK" : $"LLM connectivity FAILED: {problem}");
        }

        private void ResetSessionState(string reason)
        {
            _pendingMappingCommands = "";
            _originalProofingCommand = "";
            _lastKnownMappings = "";
            _isInTableMappingReview = false;
            _geometryAlreadyPrepared = false;
            _comClient.GeometryAlreadyPrepared = false;
            _mappingQueue = new List<(string, string)>();
            _includedIndexes = new HashSet<int>();
            _excludedIndexes = new HashSet<int>();
            _mappingReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _mappingConfidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _mappingSnapshotPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(IsQuickActionsEnabled));

            Messages.Add(new ChatMessage
            {
                Role = "Assistant",
                Content = $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n🔄 {reason} — session state cleared for this drawing.\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
            });
            BricsAI.Core.LoggerService.LogTransaction("OVERLAY", $"ResetSessionState: {reason}");
        }

        private async Task ShowLayerSnapshotAsync(MappingRow row)
        {
            PopupLayerName = row.SourceLayer;
            PopupImage = null;
            PopupStatusText = null;
            IsPopupLoading = true;
            IsPopupVisible = true;

            string? filePath = row.SnapshotPath;

            // Export on demand if no cached snapshot exists
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                try
                {
                    if (!_comClient.IsConnected) await _comClient.ConnectAsync();
                    string escapedLayer = row.SourceLayer.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string exportAction = $@"{{ ""tool_calls"": [{{ ""command_name"": ""EXPORT_LAYER_SNAPSHOT"", ""lisp_code"": ""NET:EXPORT_LAYER_SNAPSHOT:{escapedLayer}|BMP"" }}] }}";
                    IProgress<string> noop = new Progress<string>(_ => { });
                    string exportResult = await Task.Run(() => _comClient.ExecuteActionAsync(exportAction, noop));

                    // ExecuteActionAsync wraps plugin output as "Step 1: {json}" — extract just the JSON object
                    int jsonStart = exportResult.IndexOf('{');
                    if (jsonStart >= 0)
                    {
                        using var jsonDoc = JsonDocument.Parse(exportResult.Substring(jsonStart));
                        if (jsonDoc.RootElement.TryGetProperty("FilePath", out var fpElem))
                        {
                            filePath = fpElem.GetString();
                            if (!string.IsNullOrEmpty(filePath))
                                row.SnapshotPath = filePath; // Cache so next click is instant
                        }
                    }
                    else
                    {
                        // Plain error from plugin — surface it
                        throw new Exception(exportResult.Replace("Step 1: ", "").Trim());
                    }
                }
                catch (Exception ex)
                {
                    IsPopupLoading = false;
                    PopupStatusText = $"Export failed: {ex.Message}";
                    LoggerService.LogTransaction("OVERLAY", $"ShowLayerSnapshot export error for '{row.SourceLayer}': {ex.Message}");
                    return;
                }
            }

            IsPopupLoading = false;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                PopupStatusText = "No snapshot could be generated. Make sure BricsCAD is open with a drawing loaded.";
                return;
            }

            try
            {
                using var stream = File.OpenRead(filePath);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = stream;
                img.EndInit();
                img.Freeze();
                PopupImage = img;
            }
            catch (Exception ex)
            {
                PopupStatusText = $"Could not load image: {ex.Message}";
            }
        }

        private async Task ExecuteQuickAction(string overridePrompt)
        {
            if (IsBusy) return;
            // A button press is explicit user intent to start a fresh operation.
            // If we were mid-review for a previous drawing, clear that stale state now.
            if (_isInTableMappingReview)
                ResetSessionState("New action requested while mapping review was in progress");
            string originalInput = InputText;
            InputText = overridePrompt;
            _isQuickActionRun = true;
            try { await SendMessageAsync(); }
            finally { _isQuickActionRun = false; }
            InputText = originalInput; // Restore whatever they were typing
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            var userMessage = InputText;
            InputText = ""; // Clear input immediately
            if (!userMessage.Contains("_skipMappingReviewSequence_"))
                BricsAI.Core.KnowledgeService.ClearApplyFilter(); // a new request starts without a stale approval list

            Messages.Add(new ChatMessage { Role = "User", Content = userMessage });
            BricsAI.Core.LoggerService.LogUserMessage(userMessage);

            // Detect drawing changes and reset stale session state so a new proofing run
            // on a different file never inherits mappings or review state from the previous one.
            if (_comClient.IsConnected)
            {
                string currentDocPath = _comClient.GetActiveDocumentPath();
                if (!string.IsNullOrEmpty(currentDocPath) &&
                    !string.IsNullOrEmpty(_lastActiveDocumentPath) &&
                    !string.Equals(currentDocPath, _lastActiveDocumentPath, StringComparison.OrdinalIgnoreCase))
                {
                    ResetSessionState($"Drawing changed from '{System.IO.Path.GetFileName(_lastActiveDocumentPath)}' to '{System.IO.Path.GetFileName(currentDocPath)}'");
                }
                if (!string.IsNullOrEmpty(currentDocPath))
                    _lastActiveDocumentPath = currentDocPath;
            }

            try
            {
                // --- INTERACTIVE TABULAR MAPPING REVIEW INTERCEPTION ---
                if (_isInTableMappingReview && _mappingQueue.Count > 0)
                {
                    IsBusy = true;
                    SyncTargetsFromActiveTable(); // pick up any drop-down edits made in the table

                    var (reviewResponse, tokens, inputTokens, outputTokens) = await _mappingReviewAgent.ClassifyTableReviewResponseAsync(
                        userMessage, _mappingQueue, _mappingConfidence, _mappingReasons, _includedIndexes, _excludedIndexes);

                    bool mappingRowChanged = false;
                    switch (reviewResponse.Intent)
                    {
                        case "INCLUDE":
                        {
                            var included = new List<int>();
                            foreach (var idx in reviewResponse.IncludedIndexes.Any() ? reviewResponse.IncludedIndexes : reviewResponse.Indexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                {
                                    _includedIndexes.Add(idx);
                                    _excludedIndexes.Remove(idx);
                                    included.Add(idx);
                                }
                            }
                            // Auto-exclude every remaining pending row — user said "these are the ones I want"
                            for (int i = 1; i <= _mappingQueue.Count; i++)
                                if (!_includedIndexes.Contains(i) && !_excludedIndexes.Contains(i))
                                    _excludedIndexes.Add(i);
                            Messages.Add(new ChatMessage { Role = "Assistant", Content = included.Count > 0
                                ? $"✅ Row(s) {string.Join(", ", included)} included — all others excluded."
                                : "I couldn't match any valid row numbers in that — please reference the row numbers from the table." });
                            break;
                        }
                        case "EXCLUDE":
                        {
                            var excluded = new List<int>();
                            foreach (var idx in reviewResponse.ExcludedIndexes.Any() ? reviewResponse.ExcludedIndexes : reviewResponse.Indexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                {
                                    _excludedIndexes.Add(idx);
                                    _includedIndexes.Remove(idx);
                                    excluded.Add(idx);
                                }
                            }
                            // Auto-include every remaining pending row — user said "these are the ones I don't want"
                            for (int i = 1; i <= _mappingQueue.Count; i++)
                                if (!_excludedIndexes.Contains(i) && !_includedIndexes.Contains(i))
                                    _includedIndexes.Add(i);
                            Messages.Add(new ChatMessage { Role = "Assistant", Content = excluded.Count > 0
                                ? $"⏭️ Row(s) {string.Join(", ", excluded)} excluded — all others included."
                                : "I couldn't match any valid row numbers in that — please reference the row numbers from the table." });
                            break;
                        }
                        case "INCLUDE_EXCLUDE":
                        {
                            var included = new List<int>();
                            var excluded = new List<int>();
                            foreach (var idx in reviewResponse.IncludedIndexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                { _includedIndexes.Add(idx); _excludedIndexes.Remove(idx); included.Add(idx); }
                            }
                            foreach (var idx in reviewResponse.ExcludedIndexes)
                            {
                                if (idx >= 1 && idx <= _mappingQueue.Count)
                                { _excludedIndexes.Add(idx); _includedIndexes.Remove(idx); excluded.Add(idx); }
                            }
                            // Auto-exclude anything not explicitly named
                            for (int i = 1; i <= _mappingQueue.Count; i++)
                                if (!_includedIndexes.Contains(i) && !_excludedIndexes.Contains(i))
                                    _excludedIndexes.Add(i);
                            Messages.Add(new ChatMessage { Role = "Assistant", Content =
                                $"✅ Included: {(included.Any() ? string.Join(", ", included) : "none")} — Excluded: {(excluded.Any() ? string.Join(", ", excluded) : "none")} — All others excluded." });
                            break;
                        }
                        case "MEMORIZE":
                        {
                            if (!string.IsNullOrWhiteSpace(reviewResponse.RuleText))
                            {
                                // "Map the layer 'X' to standard layer 'Y'." for a layer in this review must also change
                                // the pending row — otherwise CompleteMappingReview would overwrite the memorised
                                // mapping with the old AI suggestion still sitting in the queue.
                                var ruleMatch = System.Text.RegularExpressions.Regex.Match(
                                    reviewResponse.RuleText, @"Map the layer '(.*?)' to standard layer '(.*?)'\.");
                                int rowIdx = -1;
                                string newTarget = "";
                                if (ruleMatch.Success)
                                {
                                    string src = ruleMatch.Groups[1].Value.Trim();
                                    newTarget = ruleMatch.Groups[2].Value.Trim();
                                    rowIdx = _mappingQueue.FindIndex(q => string.Equals(q.Source, src, StringComparison.OrdinalIgnoreCase));
                                    var known = BricsAI.Overlay.Models.MappingRow.StandardTargets
                                        .FirstOrDefault(t => string.Equals(t, newTarget, StringComparison.OrdinalIgnoreCase));
                                    if (known != null) newTarget = known;
                                }

                                BricsAI.Core.KnowledgeService.SaveLearning(reviewResponse.RuleText);

                                bool isBoothTarget = newTarget.StartsWith("Expo_", StringComparison.OrdinalIgnoreCase) &&
                                    (newTarget.EndsWith("BoothOutline", StringComparison.OrdinalIgnoreCase) || newTarget.EndsWith("BoothNumber", StringComparison.OrdinalIgnoreCase));

                                if (rowIdx >= 0 && !isBoothTarget)
                                {
                                    string srcName = _mappingQueue[rowIdx].Source;
                                    _mappingQueue[rowIdx] = (srcName, newTarget);
                                    _mappingReasons[srcName] = "Set by your instruction";
                                    _mappingConfidence[srcName] = "High";
                                    int row1 = rowIdx + 1;
                                    _includedIndexes.Add(row1);
                                    _excludedIndexes.Remove(row1);
                                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"🧠 Remembered, and row {row1} ('{srcName}') now maps to {newTarget} and is included.\n\n📋 The mapping review is still open — continue deciding the remaining rows." });
                                    mappingRowChanged = true;
                                }
                                else if (rowIdx >= 0)
                                {
                                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"⚠️ '{newTarget}' is a protected booth output layer — vendor layers cannot be mapped to it, so row {rowIdx + 1} was left unchanged." });
                                }
                                else
                                {
                                    Messages.Add(new ChatMessage { Role = "Assistant", Content = $"🧠 Remembered: {reviewResponse.RuleText}\n\n📋 The mapping review is still open — continue deciding the remaining rows." });
                                }
                            }
                            break;
                        }
                        case "CONFIRM_ALL":
                        {
                            for (int i = 1; i <= _mappingQueue.Count; i++)
                                if (!_excludedIndexes.Contains(i)) _includedIndexes.Add(i);
                            break;
                        }
                        case "HIGH_CONFIDENCE_ONLY":
                        {
                            int highCount = 0, lowCount = 0;
                            for (int i = 0; i < _mappingQueue.Count; i++)
                            {
                                int idx = i + 1;
                                string conf = _mappingConfidence.TryGetValue(_mappingQueue[i].Source, out var c) ? c : "Low";
                                if (conf == "High") { _includedIndexes.Add(idx); _excludedIndexes.Remove(idx); highCount++; }
                                else                { _excludedIndexes.Add(idx); _includedIndexes.Remove(idx); lowCount++;  }
                            }
                            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"✅ High-confidence rows included ({highCount}). Low-confidence rows excluded ({lowCount})." });
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
                    if (allDecided && reviewResponse.Intent == "CONFIRM_ALL")
                    {
                        // Explicit confirmation with all rows decided — proceed to proofing
                        CompleteMappingReview();
                        return;
                    }
                    else if (allDecided)
                    {
                        // All rows were auto-decided by INCLUDE/EXCLUDE/HIGH_CONFIDENCE_ONLY.
                        // Show the final table once and wait for explicit confirmation before proofing.
                        Messages.Add(new ChatMessage
                        {
                            Role = "Assistant",
                            Content = "All rows decided. Review below and reply **confirm** (or 'yes', 'looks good') to start proofing, or adjust any row.\n\n" + FormatMappingTable(),
                            IsTableContent = true,
                            MappingRows = BuildMappingRows()
                        });
                    }
                    else if (reviewResponse.Intent == "INCLUDE" || reviewResponse.Intent == "EXCLUDE" ||
                             reviewResponse.Intent == "INCLUDE_EXCLUDE" || reviewResponse.Intent == "HIGH_CONFIDENCE_ONLY" ||
                             mappingRowChanged)
                    {
                        Messages.Add(new ChatMessage { Role = "Assistant", Content = FormatMappingTable(), IsTableContent = true, MappingRows = BuildMappingRows() });
                    }

                    IsBusy = false;
                    return;
                }

            // --- INTENT GATE: typed messages must be CAD work before any agent or drawing change runs ---
            // Quick-action buttons and the internal resume-after-review call are explicit CAD intent and skip this.
            // Everything else (e.g. "hi", "what's the weather") is answered in chat and never touches BricsCAD.
            // Whether this run is the Full AI Proofing workflow. The button and the resume-after-review call are
            // explicit; typed text is judged by the intent agent (so "don't proof this yet" is NOT proofing).
            bool isFullProofing = userMessage.Contains("_skipMappingReviewSequence_") ||
                                  (_isQuickActionRun && userMessage == ProofingButtonPrompt);
            if (!_isQuickActionRun && !userMessage.Contains("_skipMappingReviewSequence_"))
            {
                IsBusy = true;
                string recent = string.Join("\n", Messages
                    .Where(m => !m.IsThinking && !m.IsTableContent)
                    .Reverse().Skip(1).Take(4).Reverse()
                    .Select(m => $"{m.Role}: {(m.Content.Length > 300 ? m.Content.Substring(0, 300) : m.Content)}"));
                var (intent, chatReply) = await _intentAgent.ClassifyAsync(userMessage, recent);
                isFullProofing = intent == IntentAgent.FullProofing;
                if (intent == IntentAgent.Chat)
                {
                    Messages.Add(new ChatMessage { Role = "Assistant", Content = chatReply });
                    IsBusy = false;
                    return;
                }
            }

            // --- CONTEXT RECOVERY: Restore previous mapping suggestions if the last session failed ---
            // If the user re-prompts proofing and we have stored mappings from a previous failed attempt,
            // skip the expensive Surveyor+Mapper loop and resume straight from the known mappings.
            bool skipMappingReviewEarly = userMessage.Contains("_skipMappingReviewSequence_");
            string cleanUserMessageEarly = userMessage.Replace("_skipMappingReviewSequence_", "").Trim();
            bool isProofingRetry = !skipMappingReviewEarly &&
                                   !string.IsNullOrEmpty(_lastKnownMappings) &&
                                   isFullProofing;

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
                        IsTableContent = true,
                        MappingRows = BuildMappingRows()
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
                var cleanupMsg = new ChatMessage { Role = "Assistant", Content = "🧹 Running cleanup: renaming non-standard layers, deleting all non-standard layers, and purging...", IsThinking = true };
                Messages.Add(cleanupMsg);
                IProgress<string> cleanupProgress = new Progress<string>(update => { cleanupMsg.Content += $"\n{update}"; });
                var cleanupStopwatch = Stopwatch.StartNew();

                try
                {
                    // Step 1: Rename every non-standard layer (not in the A2Z allow list) to Deleted_ prefix.
                    // This catches layers that were never processed by the proofing step.
                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Cleanup fastpath — step 1: RENAME_DELETED_LAYERS.");
                    string renameAction = @"{ ""tool_calls"": [{ ""command_name"": ""RENAME_DELETED_LAYERS"", ""lisp_code"": ""NET:RENAME_DELETED_LAYERS"" }] }";
                    string renameResult = await Task.Run(() => _comClient.ExecuteActionAsync(renameAction, cleanupProgress));

                    // Step 2: Delete all Deleted_ layers (now includes everything non-standard).
                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Cleanup fastpath — step 2: DELETE_LAYERS_BY_PREFIX:Deleted_.");
                    string deleteAction = @"{ ""tool_calls"": [{ ""command_name"": ""DELETE_LAYERS_BY_PREFIX"", ""lisp_code"": ""NET:DELETE_LAYERS_BY_PREFIX:Deleted_"" }] }";
                    string deleteResult = await Task.Run(() => _comClient.ExecuteActionAsync(deleteAction, cleanupProgress));

                    // Step 3: Final purge.
                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Cleanup fastpath — step 3: PURGE.");
                    string purgeAction = @"{ ""tool_calls"": [{ ""command_name"": ""PURGE"", ""lisp_code"": ""(command \""-PURGE\"" \""All\"" \""*\"" \""N\"")""  }] }";
                    string purgeResult = await Task.Run(() => _comClient.ExecuteActionAsync(purgeAction, cleanupProgress));

                    cleanupStopwatch.Stop();
                    double cleanupSeconds = Math.Round(cleanupStopwatch.Elapsed.TotalSeconds, 1);

                    cleanupMsg.IsThinking = false;
                    cleanupMsg.Content = $"✅ Cleanup complete.\n\n{renameResult}\n{deleteResult}\n{purgeResult}";
                    LoggerService.LogTransaction("PLUGIN", $"MainViewModel: Cleanup fastpath done. {renameResult} | {deleteResult}");

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

            // --- EXPLODE FASTPATH: bypass AI agents entirely for the Explode Geometry button ---
            bool isExplodeCommand = userMessage.Contains("__EXPLODE_WITH_BOOTH_LOCK__", StringComparison.Ordinal);
            if (isExplodeCommand && _comClient.IsConnected)
            {
                // Show the plan in chat before touching the drawing
                Messages.Add(new ChatMessage
                {
                    Role = "Assistant",
                    Content = "💥 **Explode Geometry — Execution Plan**\n\n" +
                              "**Step 1:** Unlock all non-frozen layers so every entity is reachable by the explode command.\n" +
                              "**Step 2:** Lock the four booth output layers (Expo_BoothOutline, Expo_BoothNumber, Expo_MaxBoothOutline, Expo_MaxBoothNumber) so they are never touched.\n" +
                              "**Step 3:** Flatten any SPLINE entities (EXPLODE cannot handle them natively).\n" +
                              "**Step 4:** Run iterative explode loop — up to 30 passes / 120 seconds — until all non-standard entities are resolved.\n\n" +
                              "Starting execution now..."
                });

                var explodeMsg = new ChatMessage { Role = "Assistant", Content = "⚙️ Running explode with booth protection...", IsThinking = true };
                Messages.Add(explodeMsg);
                IProgress<string> explodeProgress = new Progress<string>(update => { explodeMsg.Content += $"\n{update}"; });
                var explodeStopwatch = Stopwatch.StartNew();

                try
                {
                    LoggerService.LogTransaction("PLUGIN", "MainViewModel: Explode fastpath — sending NET:EXPLODE_WITH_BOOTH_LOCK.");
                    string explodeAction = @"{ ""tool_calls"": [{ ""command_name"": ""EXPLODE_WITH_BOOTH_LOCK"", ""lisp_code"": ""NET:EXPLODE_WITH_BOOTH_LOCK"" }] }";
                    string explodeResult = await Task.Run(() => _comClient.ExecuteActionAsync(explodeAction, explodeProgress));

                    explodeStopwatch.Stop();
                    double explodeSeconds = Math.Round(explodeStopwatch.Elapsed.TotalSeconds, 1);

                    explodeMsg.IsThinking = false;
                    explodeMsg.Content = $"✅ Explode complete.\n\n{explodeResult}";
                    LoggerService.LogTransaction("PLUGIN", $"MainViewModel: Explode fastpath done in {explodeSeconds}s. {explodeResult}");

                    Messages.Add(new ChatMessage
                    {
                        Role = "Assistant",
                        Content = $"📊 Performance: 0 API tokens consumed (explode runs natively via COM — no AI calls needed). Task completed in {explodeSeconds} seconds."
                    });
                }
                catch (Exception ex)
                {
                    explodeMsg.IsThinking = false;
                    explodeMsg.Content = $"⚠️ Explode error: {ex.Message}";
                    LoggerService.LogTransaction("PLUGIN", $"MainViewModel: Explode fastpath error: {ex.Message}");
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

            // --- PRE-SURVEY EXPLODE ---
            // XRef-bound and block geometry lives on collapsed/merged layers (e.g. a single "XRef" layer)
            // until the entities are exploded. Surveying before exploding means the AI proposes mappings
            // based on incomplete layer data and then misses the real per-object layers revealed post-explode.
            // Fix: explode first, then survey the full expanded layer set.
            bool isProofingLike = isFullProofing;
            if (isProofingLike && !skipMappingReviewEarly && !_geometryAlreadyPrepared && _comClient.IsConnected)
            {
                var preSurveyMsg = new ChatMessage
                {
                    Role = "Assistant",
                    Content = "💥 **Pre-Analysis Explode** — Exploding XRef/block geometry first so all real layers are visible before surveying...",
                    IsThinking = true
                };
                Messages.Add(preSurveyMsg);
                IProgress<string> preSurveyProgress = new Progress<string>(update => { preSurveyMsg.Content += $"\n{update}"; });

                LoggerService.LogTransaction("PLUGIN", "MainViewModel: Pre-survey explode — locking booth layers then PREPARE_GEOMETRY.");
                string lockAction = @"{ ""tool_calls"": [{ ""command_name"": ""LOCK_BOOTH_LAYERS"", ""lisp_code"": ""NET:LOCK_BOOTH_LAYERS"" }] }";
                string prepAction = @"{ ""tool_calls"": [{ ""command_name"": ""PREPARE_GEOMETRY"",  ""lisp_code"": ""NET:PREPARE_GEOMETRY"" }] }";

                await Task.Run(() => _comClient.ExecuteActionAsync(lockAction, preSurveyProgress));
                string preSurveyResult = await Task.Run(() => _comClient.ExecuteActionAsync(prepAction, preSurveyProgress));

                preSurveyMsg.IsThinking = false;
                preSurveyMsg.Content = $"✅ Geometry exploded — all layers now expanded for analysis.\n{preSurveyResult}";

                _geometryAlreadyPrepared = true;
                _comClient.GeometryAlreadyPrepared = true;
                LoggerService.LogTransaction("PLUGIN", $"MainViewModel: Pre-survey explode done. {preSurveyResult}");
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

            string _dwgFile = _comClient.IsConnected ? System.IO.Path.GetFileName(_comClient.GetActiveDocumentPath()) : "(unknown)";
            if (string.IsNullOrWhiteSpace(_dwgFile)) _dwgFile = "(unknown)";
            BricsAI.Core.LoggerService.LogTransaction("SESSION", $"Proofing started | File: {_dwgFile} | Prompt: {userMessage.Replace(Environment.NewLine, " ")}");

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

            // Summary/BOM commands are read-only — they must not enter the interactive mapping review
            // gate (which locks Quick Action buttons until the user confirms/aborts). The Executor
            // handles summary requests via NET:MESSAGE plans and completes without locking the UI.
            bool isSummaryOnly = cleanUserMessage.Contains("Bill of Materials", StringComparison.OrdinalIgnoreCase) ||
                                 cleanUserMessage.Contains("Audit Summary", StringComparison.OrdinalIgnoreCase) ||
                                 cleanUserMessage.Contains("I don't need macros run", StringComparison.OrdinalIgnoreCase);

            // The mapping-review grid belongs to the Full AI Proofing workflow only. Other requests
            // (delete a layer, hide layers, select columns, questions about the drawing...) never show it.
            bool isProofingRequest = isFullProofing;

            // Trigger the mapper for a proofing request when layers need review and the command is not a
            // pure memory instruction or a read-only summary request.
            if (isProofingRequest && unknownLayers.Any() && !skipMappingReview && !isMemoryInstruction && !isSummaryOnly)
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

                        // === PHASE 3: Visual verification for low-confidence geometry results ===
                        var lowConfidence = phase2.Mappings
                            .Where(m => m.Confidence == "Low")
                            .ToList();

                        if (lowConfidence.Any())
                        {
                            mapProgress.Report($"\n📸 Phase 3: Exporting snapshots for {lowConfidence.Count} low-confidence layer(s)...");
                            var visualInputs = new List<(string LayerName, string ProposedTarget, string SnapshotPath)>();

                            foreach (var m in lowConfidence)
                            {
                                mapProgress.Report($"\n   Exporting snapshot for '{m.SourceLayer}'...");
                                string safeLayerName = m.SourceLayer.Replace("\"", "\\\"").Replace("\\", "\\\\");
                                string snapshotPlan = $@"{{ ""tool_calls"": [{{ ""command_name"": ""EXPORT_SNAPSHOT"", ""lisp_code"": ""NET:EXPORT_LAYER_SNAPSHOT:{safeLayerName}|PNG"" }}] }}";
                                string snapshotResult = await Task.Run(() => _comClient.ExecuteActionAsync(snapshotPlan, mapProgress));

                                if (snapshotResult.Contains("FilePath") && !snapshotResult.StartsWith("Error"))
                                {
                                    try
                                    {
                                        using var doc = System.Text.Json.JsonDocument.Parse(snapshotResult);
                                        if (doc.RootElement.TryGetProperty("FilePath", out var fp) &&
                                            doc.RootElement.TryGetProperty("FormatUsed", out var fmt))
                                        {
                                            string filePath = fp.GetString() ?? "";
                                            string formatUsed = fmt.GetString() ?? "";
                                            if (System.IO.File.Exists(filePath))
                                            {
                                                // Always store for tooltip hover (WPF BitmapImage handles BMP fine)
                                                _mappingSnapshotPaths[m.SourceLayer] = filePath;
                                                // Claude Vision only supports PNG/JPEG/GIF/WebP — skip BMP for classification
                                                if (!string.Equals(formatUsed, "BMP", StringComparison.OrdinalIgnoreCase))
                                                    visualInputs.Add((m.SourceLayer, m.TargetLayer, filePath));
                                                else
                                                    mapProgress.Report($"   ⚠️ Snapshot fell back to BMP for '{m.SourceLayer}' — hover preview available, skipping visual verification.");
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                else
                                {
                                    mapProgress.Report($"   ⚠️ Could not export snapshot for '{m.SourceLayer}' — skipping.");
                                }
                            }

                            if (visualInputs.Any())
                            {
                                mapProgress.Report($"\n👁️ Visually verifying {visualInputs.Count} layer(s) with image analysis...");
                                var phase3 = await _mapper.VisuallyVerifyLowConfidenceAsync(visualInputs);
                                totalTokens += phase3.Tokens;
                                totalInputTokens += phase3.InputTokens;
                                totalOutputTokens += phase3.OutputTokens;

                                // Replace the low-confidence mappings with visually verified results
                                var phase3BySource = phase3.Verified.ToDictionary(r => r.SourceLayer, StringComparer.OrdinalIgnoreCase);
                                allMappings.RemoveAll(r => r.Confidence == "Low" && phase3BySource.ContainsKey(r.SourceLayer));
                                allMappings.AddRange(phase3.Verified);

                                int upgraded = phase3.Verified.Count(r => r.Confidence == "High");
                                mapProgress.Report($"✅ Visual verification complete: {upgraded}/{phase3.Verified.Count} confirmed, confidence upgraded.");
                            }
                        }
                    }
                }

                mapperMsg.IsThinking = false;

                // Build the pending tool calls JSON from all results
                var pendingToolCalls = allMappings
                    .Select(m => $@"{{ ""command_name"": ""Semantic Mapping"", ""lisp_code"": ""{m.LispCode}"" }}")
                    .ToList();

                // Store reasons and confidence keyed by source layer
                _mappingReasons.Clear();
                _mappingConfidence.Clear();
                foreach (var m in allMappings)
                {
                    if (!string.IsNullOrEmpty(m.Reason))
                        _mappingReasons[m.SourceLayer] = m.Reason;
                    _mappingConfidence[m.SourceLayer] = m.Confidence;
                }

                // Also list every layer that already has a learned mapping, pre-selected to that mapping, so
                // nothing is moved silently: the reviewer sees (and can change or reject) every remap.
                var learnedToolCalls = BuildLearnedToolCalls(cleanLayersPayload, standardA2zLayers, knownMappings, unknownLayers);
                pendingToolCalls.AddRange(learnedToolCalls);

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
                            Content = $"🛑 **Human Review Required** — {_mappingQueue.Count} layer(s) to review ({unknownLayers.Count} new, {learnedToolCalls.Count} from knowledge base)\n{SkippedLayersNote}\n\n{FormatMappingTable()}",
                            IsTableContent = true,
                            MappingRows = BuildMappingRows()
                        });
                    }

                    OnPropertyChanged(nameof(IsQuickActionsEnabled));
                    IsBusy = false; // Unlock UI to allow user feedback
                    return; // Halt execution and wait for human response
                }
                else
                {
                    // Phase 1/2/3 returned 0 mappings — build fallback "Deleted_" proposals so
                    // the user still gets the tabular review and can manually assign targets.
                    foreach (var layer in unknownLayers)
                    {
                        _mappingReasons[layer] = "Layer could not be classified automatically by name or geometry — defaulting to Deleted_";
                        _mappingConfidence[layer] = "Low";
                    }

                    var fallbackToolCalls = unknownLayers
                        .Select(layer =>
                        {
                            string s = layer.Replace("\\", "\\\\").Replace("\"", "\\\"");
                            return $@"{{ ""command_name"": ""Semantic Mapping"", ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:{s}:Deleted_"" }}";
                        })
                        .ToList();
                    fallbackToolCalls.AddRange(BuildLearnedToolCalls(cleanLayersPayload, standardA2zLayers, knownMappings, unknownLayers));

                    _pendingMappingCommands = "{ \"tool_calls\": [\n" + string.Join(",\n", fallbackToolCalls) + "\n] }";
                    _lastKnownMappings = _pendingMappingCommands;
                    _originalProofingCommand = userMessage;
                    _isInTableMappingReview = true;
                    _mappingQueue = ExtractMappingPairsFromJson(_pendingMappingCommands);
                    _includedIndexes.Clear();
                    _excludedIndexes.Clear();

                    if (_mappingQueue.Count > 0)
                    {
                        stopwatch.Stop();
                        double surveySeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
                        Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 {unknownLayers.Count} layer(s) could not be automatically classified. Survey completed in {surveySeconds}s." });
                        Messages.Add(new ChatMessage
                        {
                            Role = "Assistant",
                            Content = $"🛑 **Human Review Required** — {_mappingQueue.Count} layer(s) unclassified (all defaulting to Deleted_)\n\n" +
                                      $"The AI could not determine targets for these layers. Adjust any you want to remap, then confirm.\n\n{FormatMappingTable()}",
                            IsTableContent = true,
                            MappingRows = BuildMappingRows()
                        });
                    }

                    OnPropertyChanged(nameof(IsQuickActionsEnabled));
                    IsBusy = false;
                    return;
                }
            }

            // --- TABULAR REVIEW FOR DB-KNOWN LAYERS ---
            // Mapper was skipped (all non-standard layers matched the DB). Show the same tabular
            // review so the user can confirm or adjust before any destructive proofing runs.
            bool isProofingCommand = isFullProofing;
            if (isProofingCommand && !skipMappingReview)
            {
                var dbLayerNames = cleanLayersPayload
                    .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !standardA2zLayers.Contains(l))
                    .Distinct()
                    .ToList();

                _mappingReasons.Clear();
                _mappingConfidence.Clear();

                var dbToolCalls = new List<string>();
                foreach (var layer in dbLayerNames)
                {
                    string target = knownMappings.TryGetValue(layer, out string t) ? t : "Deleted_";
                    string reason = knownMappings.ContainsKey(layer)
                        ? "Previously learned from knowledge base"
                        : "No matching rule found — will be renamed to Deleted_";
                    string confidence = knownMappings.ContainsKey(layer) ? "High" : "Low";
                    string safeLisp   = layer.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string safeTarget = target.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    _mappingReasons[layer]    = reason;
                    _mappingConfidence[layer] = confidence;
                    dbToolCalls.Add($@"{{ ""command_name"": ""Semantic Mapping"", ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:{safeLisp}:{safeTarget}"" }}");
                }

                _pendingMappingCommands = "{ \"tool_calls\": [\n" + string.Join(",\n", dbToolCalls) + "\n] }";
                _lastKnownMappings     = _pendingMappingCommands;
                _originalProofingCommand = cleanUserMessage;
                _isInTableMappingReview  = true;
                _mappingQueue = ExtractMappingPairsFromJson(_pendingMappingCommands);
                _includedIndexes.Clear();
                _excludedIndexes.Clear();

                if (_mappingQueue.Count > 0)
                {
                    Messages.Add(new ChatMessage
                    {
                        Role = "Assistant",
                        Content = $"⚠️ **Review Required** — {_mappingQueue.Count} layer(s) matched from knowledge base\n\n" +
                                  $"These layers were matched from previous learning. Confirm or adjust before proofing.\n\n{FormatMappingTable()}",
                        IsTableContent = true,
                        MappingRows = BuildMappingRows()
                    });
                }

                OnPropertyChanged(nameof(IsQuickActionsEnabled));
                IsBusy = false;
                return;
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

            BricsAI.Core.KnowledgeService.ClearApplyFilter();
            stopwatch.Stop();
            double seconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 1);
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"📊 Performance: {totalTokens} API tokens consumed ({totalInputTokens} Input, {totalOutputTokens} Output). Task completed in {seconds} seconds." });

            BricsAI.Core.LoggerService.LogTransaction("SESSION",
                $"Proofing complete | File: {_dwgFile} | Tokens: {totalTokens} total ({totalInputTokens} input, {totalOutputTokens} output) | Duration: {seconds}s");

            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_debug_log.txt");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (var msg in Messages) sb.AppendLine($"[{msg.Role}]: {msg.Content}");
                System.IO.File.AppendAllText(logPath, $"\n\n--- Run: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {_dwgFile} ---\n" + sb.ToString());
            }
            catch { }

            IsBusy = false;
        }
        catch (LlmUnavailableException ex)
        {
            // The AI is required for every step; stop instead of continuing with empty/guessed results.
            Messages.Add(new ChatMessage { Role = "Assistant", Content = $"🔌 **Cannot reach the AI service — nothing was processed.**\n{ex.Message}" });
            LoggerService.LogTransaction("OVERLAY", $"LLM unavailable: {ex.Message}");
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
            SyncTargetsFromActiveTable();
            DeactivateTables();

            int acceptedCount = 0;
            for (int i = 1; i <= _mappingQueue.Count; i++)
            {
                var (source, target) = _mappingQueue[i - 1];
                if (string.Equals(target, BricsAI.Overlay.Models.MappingRow.NoneTarget, StringComparison.OrdinalIgnoreCase))
                {
                    // Reviewer chose "None": make sure no stale learned mapping survives for this layer.
                    BricsAI.Core.KnowledgeService.RemoveMapping(source);
                    continue;
                }
                if (_includedIndexes.Contains(i))
                {
                    BricsAI.Core.KnowledgeService.SaveLearning($"Map the layer '{source}' to standard layer '{target}'.");
                    acceptedCount++;
                }
            }
            int skippedCount = _mappingQueue.Count - acceptedCount;

            // Only the approved rows may be remapped in the drawing; learned mappings the reviewer
            // unticked or set to None must not be applied silently by APPLY_LAYER_MAPPINGS.
            BricsAI.Core.KnowledgeService.SetApplyFilter(
                Enumerable.Range(1, _mappingQueue.Count)
                    .Where(i => _includedIndexes.Contains(i) &&
                                !string.Equals(_mappingQueue[i - 1].Target, BricsAI.Overlay.Models.MappingRow.NoneTarget, StringComparison.OrdinalIgnoreCase))
                    .Select(i => _mappingQueue[i - 1].Source)
                    .ToList());

            _isInTableMappingReview = false;
            _mappingQueue.Clear();
            _mappingReasons.Clear();
            _mappingConfidence.Clear();
            _mappingSnapshotPaths.Clear();
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
            DeactivateTables();
            _isInTableMappingReview = false;
            _mappingQueue.Clear();
            _mappingReasons.Clear();
            _mappingConfidence.Clear();
            _mappingSnapshotPaths.Clear();
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
        /// Returns the instruction text shown below the mapping table. The actual row data
        /// is now rendered by the XAML ItemsControl via BuildMappingRows().
        /// </summary>
        private string FormatMappingTable() =>
            "Tick the rows to map (header checkbox selects all), change any Target Layer from its drop-down " +
            "(choose None to skip a layer), then press \"Apply selected & proceed\".\n" +
            "Hover a layer name to preview its snapshot, or a reason to read it in full.\n\n" +
            "You can also type instead: \"include 1,3,5\", \"exclude 2,4\", \"confirm all\", \"cancel\", " +
            "or a rule to remember.";

        /// <summary>
        /// Builds the structured row list for the interactive mapping table in the UI.
        /// </summary>
        private List<BricsAI.Overlay.Models.MappingRow> BuildMappingRows()
        {
            // A new table supersedes any earlier one: freeze the old one (carry its edits first).
            SyncTargetsFromActiveTable();
            DeactivateTables();

            var rows = new List<BricsAI.Overlay.Models.MappingRow>();
            for (int i = 0; i < _mappingQueue.Count; i++)
            {
                int idx = i + 1;
                var (source, target) = _mappingQueue[i];
                string confidence = _mappingConfidence.TryGetValue(source, out var c) ? c : "Low";
                string reason = _mappingReasons.TryGetValue(source, out var r) && !string.IsNullOrWhiteSpace(r) ? r : "—";
                string status = _includedIndexes.Contains(idx) ? "included" : _excludedIndexes.Contains(idx) ? "excluded" : "pending";
                _mappingSnapshotPaths.TryGetValue(source, out var snapshotPath);
                var row = new BricsAI.Overlay.Models.MappingRow
                {
                    Index      = idx,
                    SourceLayer = source,
                    Confidence  = confidence,
                    Reason      = reason,
                    SnapshotPath = snapshotPath
                };
                row.Init(target, _includedIndexes.Contains(idx), status);
                rows.Add(row);
            }
            return rows;
        }

        private const string SkippedLayersNote =
            "Not listed: frozen layers, layers 0 / Defpoints, the booth output layers (Expo_BoothOutline, Expo_BoothNumber, Expo_MaxBoothOutline, Expo_MaxBoothNumber) and the other standard Expo_ layers.";

        /// <summary>
        /// Builds review rows for layers in the drawing that already have a learned mapping (High confidence,
        /// pre-selected to that mapping). Registers reason/confidence for each.
        /// </summary>
        private List<string> BuildLearnedToolCalls(string layersPayload, HashSet<string> standardLayers,
            Dictionary<string, string> known, IEnumerable<string> alreadyListed)
        {
            var listed = new HashSet<string>(alreadyListed, StringComparer.OrdinalIgnoreCase);
            var calls = new List<string>();
            var layers = layersPayload
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !standardLayers.Contains(l) && !listed.Contains(l) && known.ContainsKey(l))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in layers)
            {
                _mappingReasons[layer] = "Previously learned from knowledge base";
                _mappingConfidence[layer] = "High";
                string src = layer.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string tgt = known[layer].Replace("\\", "\\\\").Replace("\"", "\\\"");
                calls.Add($@"{{ ""command_name"": ""Semantic Mapping"", ""lisp_code"": ""NET:LEARN_LAYER_MAPPING:{src}:{tgt}"" }}");
            }
            return calls;
        }

        private ChatMessage? GetActiveTable() =>
            Messages.LastOrDefault(m => m.HasMappingRows && m.IsActive);

        private void DeactivateTables()
        {
            foreach (var m in Messages.Where(m => m.HasMappingRows && m.IsActive)) m.IsActive = false;
        }

        /// <summary>Copies target-layer drop-down choices from the live table back into the review queue.</summary>
        private void SyncTargetsFromActiveTable()
        {
            var rows = GetActiveTable()?.MappingRows;
            if (rows == null) return;
            foreach (var row in rows)
            {
                int i = row.Index - 1;
                if (i >= 0 && i < _mappingQueue.Count && _mappingQueue[i].Source == row.SourceLayer)
                    _mappingQueue[i] = (_mappingQueue[i].Source, row.TargetLayer);
            }
        }

        /// <summary>"Apply selected" button: checked rows (with a real target) are included, everything else excluded.</summary>
        private void ApplyTableSelection()
        {
            if (IsBusy || !_isInTableMappingReview) return;
            var table = GetActiveTable();
            if (table?.MappingRows == null) return;

            SyncTargetsFromActiveTable();
            _includedIndexes.Clear();
            _excludedIndexes.Clear();
            foreach (var row in table.MappingRows)
            {
                if (row.IsSelected && row.IsMappable) _includedIndexes.Add(row.Index);
                else _excludedIndexes.Add(row.Index);
            }

            if (_includedIndexes.Count == 0)
            {
                Messages.Add(new ChatMessage { Role = "Assistant", Content = "No rows are checked. Tick the rows you want mapped (or use the header checkbox to select all), then press **Apply selected**. Rows whose target is None are never mapped." });
                return;
            }
            CompleteMappingReview();
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
