using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace BricsAI.Overlay.Services
{
    public class ComClient
    {
        private dynamic? _acadApp;
        private readonly PluginManager _pluginManager = new PluginManager();

        public bool IsConnected => _acadApp != null;

        // P/Invoke for GetActiveObject
        [DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IDispatch)] out object? ppunk);

        private static object? GetActiveObject(string progId)
        {
            try
            {
                Type? t = Type.GetTypeFromProgID(progId);
                if (t == null) return null;
                
                Guid clsid = t.GUID;
                GetActiveObject(ref clsid, IntPtr.Zero, out object? obj);
                return obj;
            }
            catch (Exception)
            {
                return null;
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<string> SendCommandAsync(string command)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                // Connect if not already connected
                if (_acadApp == null)
                {
                    // Try BricsCAD first
                    _acadApp = GetActiveObject("BricscadApp.AcadApplication");

                    if (_acadApp == null)
                    {
                        // Try AutoCAD fallback
                       _acadApp = GetActiveObject("AutoCAD.Application");
                    }

                    if (_acadApp == null)
                    {
                        return "Error: Could not connect to BricsCAD. Is it running?";
                    }
                }

                if (command.StartsWith("NET:"))
                {
                    var plugin = _pluginManager.GetPluginForCommand(command, MajorVersion);
                    if (plugin != null)
                    {
                        return plugin.Execute(_acadApp.ActiveDocument, command);
                    }
                    return $"WARNING Unrecognized or Unsupported NET command: {command}";
                }
                
                // Standard LISP command
                object? ignore = _acadApp!.ActiveDocument.SendCommand(command + "\n");
                return "Command sent.";
            }
            catch (Exception ex)
            {
                _acadApp = null; // Reset connection on failure
                return $"Error executing command: {ex.Message}";
            }
        }

        public int MajorVersion { get; private set; }

        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                if (_acadApp != null) return true;

                try
                {
                    // Try BricsCAD first
                    _acadApp = GetActiveObject("BricscadApp.AcadApplication");
                    if (_acadApp != null)
                    {
                        DetectVersion();
                        _pluginManager.LoadPlugins();
                        return true;
                    }

                    // Try AutoCAD fallback
                    _acadApp = GetActiveObject("AutoCAD.Application");
                    if (_acadApp != null)
                    {
                        DetectVersion();
                        _pluginManager.LoadPlugins();
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void ForceUnlockAllLayersExceptBoothLayersSynchronously()
        {
            try
            {
                if (_acadApp?.ActiveDocument?.Layers != null)
                {
                    var keepLocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Expo_BoothOutline",
                        "Expo_BoothNumber",
                        "Expo_MaxBoothOutline",
                        "Expo_MaxBoothNumber"
                    };

                    var layers = _acadApp.ActiveDocument.Layers;
                    for (int i = 0; i < layers.Count; i++)
                    {
                        try
                        {
                            var layer = layers.Item(i);
                            string name = layer.Name;
                            if (!keepLocked.Contains(name))
                            {
                                layer.Lock = false;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void DetectVersion()
        {
            try
            {
                string? versionStr = _acadApp?.Version;
                if (versionStr != null)
                {
                    // Extract just the leading digits (e.g., "24.1s (x64)" -> "24")
                    var match = System.Text.RegularExpressions.Regex.Match(versionStr, @"^\d+");
                    if (match.Success && int.TryParse(match.Value, out int v))
                    {
                        MajorVersion = v;
                    }
                    else
                    {
                        MajorVersion = 19;
                    }
                }
                else
                {
                    MajorVersion = 19;
                }
            }
            catch
            {
                MajorVersion = 19; // Default to V19 if detection fails (modern safe)
            }
        }

        public async Task<string> ExecuteActionAsync(string actionJson, System.IProgress<string>? progress = null)
        {
            if (_acadApp == null && !await ConnectAsync())
            {
                string err = "Error: Could not connect to BricsCAD.";
                progress?.Report($"❌ {err}");
                return err;
            }

            try
            {
                // Simple JSON parsing (avoiding full serializer overhead inside ComClient if possible, but we need it here)
                // Assuming format: {"command": "LAYERSPANELOPEN", "lisp_code": "..."}
                // or the user's schema: command_name, lisp_code, target_version
                
                using (var doc = JsonDocument.Parse(actionJson))
                {
                    var root = doc.RootElement;
                    
                    // Check for "tool_calls" array
                    if (root.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == JsonValueKind.Array)
                    {
                        var results = new List<string>();
                        int step = 1;
                        bool renameDeletedRanThisBatch = false; // guard: RENAME_DELETED_LAYERS ran — block auto-delete in same batch

                        // Proofing completeness pre-scan: detect if this is a proofing batch that is
                        // missing its mandatory final steps. The AI occasionally omits them.
                        bool batchHasApplyLayerMappings = false;
                        bool batchHasRenameDeleted = false;
                        bool batchHasLockBooth = false;
                        foreach (var scanTool in tools.EnumerateArray())
                        {
                            string scanLisp = (scanTool.TryGetProperty("lisp_code", out var sle) ? sle.GetString() : null) ?? "";
                            string scanCmd  = (scanTool.TryGetProperty("command_name", out var sce) ? sce.GetString() : null) ?? "";
                            string scanCombined = scanLisp + " " + scanCmd;
                            if (scanCombined.Contains("APPLY_LAYER_MAPPINGS")) batchHasApplyLayerMappings = true;
                            if (scanCombined.Contains("RENAME_DELETED_LAYERS")) batchHasRenameDeleted = true;
                            if (scanCombined.Contains("LOCK_BOOTH_LAYERS"))     batchHasLockBooth = true;
                        }

                        // Proofing pre-guard: if this is a proofing batch but LOCK_BOOTH_LAYERS is missing,
                        // execute it FIRST (before PREPARE_GEOMETRY) to protect booth layers throughout the sequence.
                        if (batchHasApplyLayerMappings && !batchHasLockBooth && _acadApp?.ActiveDocument != null)
                        {
                            string guardMsg = $"PROOFING GUARD: NET:LOCK_BOOTH_LAYERS was missing from plan — executing at the START to protect booth layers.";
                            progress?.Report($"⚠️ {guardMsg}");
                            BricsAI.Core.LoggerService.LogTransaction("GUARD", guardMsg);
                            var lockPlugin = _pluginManager.GetPluginForCommand("NET:LOCK_BOOTH_LAYERS", MajorVersion);
                            if (lockPlugin != null)
                            {
                                string lockResult = lockPlugin.Execute(_acadApp!.ActiveDocument, "NET:LOCK_BOOTH_LAYERS");
                                BricsAI.Core.LoggerService.LogComResponse(lockResult);
                                results.Add($"Step {step++} [AUTO]: {lockResult}");
                                progress?.Report($"✅ {lockResult}\n");
                                batchHasLockBooth = true;
                            }
                        }

                        foreach (var tool in tools.EnumerateArray())
                        {
                            string? lispCode = tool.TryGetProperty("lisp_code", out var lisp) ? lisp.GetString() : null;
                            string? commandName = tool.TryGetProperty("command_name", out var cmd) ? cmd.GetString() : null;

                            string netCmd = "";
                            if (!string.IsNullOrEmpty(lispCode) && lispCode!.Contains("NET:")) 
                                netCmd = lispCode!.Substring(lispCode.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                            else if (!string.IsNullOrEmpty(commandName) && commandName!.Contains("NET:")) 
                                netCmd = commandName!.Substring(commandName.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                            if (!string.IsNullOrEmpty(netCmd) && _acadApp?.ActiveDocument != null)
                            {
                                if (netCmd.StartsWith("NET:MESSAGE:"))
                                {
                                    string msg = netCmd.Substring("NET:MESSAGE:".Length).Trim();
                                    string ret = $"MESSAGE: {msg}";
                                    results.Add(ret);
                                    progress?.Report($"💬 {msg}");
                                }
                                else
                                {
                                    // Safety guard: if RENAME_DELETED_LAYERS already ran in this batch,
                                    // block any DELETE_LAYERS_BY_PREFIX that the AI appended to the proofing
                                    // sequence. The layers were just renamed — deleting them immediately
                                    // defeats the purpose and can destroy block geometry via the CHPROP pass.
                                    if (renameDeletedRanThisBatch && netCmd!.StartsWith("NET:DELETE_LAYERS_BY_PREFIX"))
                                    {
                                        string skipped = $"Step {step++}: SKIPPED {netCmd} — DELETE_LAYERS_BY_PREFIX is not allowed in a proofing sequence immediately after RENAME_DELETED_LAYERS.";
                                        results.Add(skipped);
                                        BricsAI.Core.LoggerService.LogTransaction("PLUGIN", skipped);
                                        progress?.Report($"⚠️ Skipped auto-delete of Deleted_ layers (use a separate request to delete them)\n");
                                        continue;
                                    }

                                    // Defense-in-depth: block LEARN_LAYER_MAPPING from running inside a
                                    // proofing batch. The Executor should never put these in a proofing
                                    // sequence, but if it does, verify this is not a proofing run before
                                    // allowing the mapping to be permanently saved.
                                    if (batchHasApplyLayerMappings && netCmd!.StartsWith("NET:LEARN_LAYER_MAPPING"))
                                    {
                                        string skipped = $"Step {step++}: SKIPPED {netCmd} — LEARN_LAYER_MAPPING is not allowed inside a proofing sequence. Use a dedicated 'remember' command instead.";
                                        results.Add(skipped);
                                        BricsAI.Core.LoggerService.LogTransaction("GUARD", skipped);
                                        progress?.Report($"⚠️ Skipped unsolicited LEARN_LAYER_MAPPING inside proofing batch\n");
                                        continue;
                                    }

                                    var plugin = _pluginManager.GetPluginForCommand(netCmd, MajorVersion);
                                    if (plugin != null)
                                    {
                                        string pluginName = plugin.Name ?? "Unknown Plugin";
                                        string commandText = netCmd ?? "";
                                        progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Executing {pluginName}...");
                                        BricsAI.Core.LoggerService.LogComExecution(pluginName, commandText);
                                        string executeResult = plugin.Execute(_acadApp!.ActiveDocument, netCmd);
                                        BricsAI.Core.LoggerService.LogComResponse(executeResult);
                                        // Track that RENAME_DELETED_LAYERS ran so we can guard DELETE in same batch
                                        if (netCmd!.StartsWith("NET:RENAME_DELETED_LAYERS"))
                                            renameDeletedRanThisBatch = true;
                                        string logEntry = $"Step {step}: {executeResult}";
                                        results.Add(logEntry);
                                        progress?.Report($"✅ {executeResult}\n");
                                        step++;
                                    }
                                    else
                                    {
                                        string err = $"Step {step++}: WARNING Unrecognized or Unsupported NET command: {netCmd}";
                                        results.Add(err);
                                        progress?.Report($"⚠️ Unsupported Tool: {netCmd}\n");
                                    }
                                }
                            }
                            else if (!string.IsNullOrEmpty(lispCode) && _acadApp?.ActiveDocument != null)
                            {
                                progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Sending Native Command...");
                                BricsAI.Core.LoggerService.LogComExecution("Native LISP", lispCode!);
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(lispCode + "\n");
                                BricsAI.Core.LoggerService.LogComResponse("Command Sent Natively");
                                string res = $"Step {step++}: Executed LISP [{lispCode}]";
                                results.Add(res);
                                progress?.Report($"✅ Completed Native String\n");
                            }
                            else if (!string.IsNullOrEmpty(commandName) && _acadApp?.ActiveDocument != null)
                            {
                                progress?.Report($"🛠️ [{step}/{tools.GetArrayLength()}] Sending Command {commandName}...");
                                BricsAI.Core.LoggerService.LogComExecution(commandName!, commandName!);
                                object? ignore = _acadApp!.ActiveDocument.SendCommand(commandName + "\n");
                                BricsAI.Core.LoggerService.LogComResponse("Command Sent Natively");
                                string res = $"Step {step++}: Executed {commandName}";
                                results.Add(res);
                                progress?.Report($"✅ Completed {commandName}\n");
                            }
                        }

                        // Proofing completeness guard: if this was a proofing batch (contained APPLY_LAYER_MAPPINGS)
                        // and the AI omitted the mandatory RENAME_DELETED_LAYERS, append it now at the end.
                        // (LOCK_BOOTH_LAYERS is now executed at the START via the pre-guard above.)
                        if (batchHasApplyLayerMappings && _acadApp?.ActiveDocument != null)
                        {
                            if (!batchHasRenameDeleted && !renameDeletedRanThisBatch)
                            {
                                string guardMsg = $"PROOFING GUARD: NET:RENAME_DELETED_LAYERS was missing from plan — executing now.";
                                progress?.Report($"⚠️ {guardMsg}");
                                BricsAI.Core.LoggerService.LogTransaction("GUARD", guardMsg);
                                var renamePlugin = _pluginManager.GetPluginForCommand("NET:RENAME_DELETED_LAYERS", MajorVersion);
                                if (renamePlugin != null)
                                {
                                    string renameResult = renamePlugin.Execute(_acadApp!.ActiveDocument, "NET:RENAME_DELETED_LAYERS");
                                    BricsAI.Core.LoggerService.LogComResponse(renameResult);
                                    results.Add($"Step {step++} [AUTO]: {renameResult}");
                                    progress?.Report($"✅ {renameResult}\n");
                                    renameDeletedRanThisBatch = true;
                                    batchHasRenameDeleted = true;
                                }
                            }
                        }

                        string finalResult = string.Join("\n", results);
                        return finalResult;
                    }
                    
                    // Fallback for direct object (legacy/single tool)
                    JsonElement singleTool = root;
                    string? sLisp = singleTool.TryGetProperty("lisp_code", out var sl) ? sl.GetString() : null;
                    string? sCmd = singleTool.TryGetProperty("command_name", out var sc) ? sc.GetString() : null;

                    string netCmdSingle = "";
                    if (!string.IsNullOrEmpty(sLisp) && sLisp!.Contains("NET:")) 
                        netCmdSingle = sLisp.Substring(sLisp.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');
                    else if (!string.IsNullOrEmpty(sCmd) && sCmd!.Contains("NET:")) 
                        netCmdSingle = sCmd.Substring(sCmd.IndexOf("NET:")).TrimEnd(')', ' ', '\n', '\r');

                    if (!string.IsNullOrEmpty(netCmdSingle) && _acadApp?.ActiveDocument != null)
                    {
                        if (netCmdSingle.StartsWith("NET:MESSAGE:"))
                        {
                            string msg = netCmdSingle.Substring("NET:MESSAGE:".Length).Trim();
                            progress?.Report($"💬 {msg}");
                            return msg;
                        }

                        var plugin = _pluginManager.GetPluginForCommand(netCmdSingle, MajorVersion);
                        if (plugin != null)
                        {
                            string pluginName = plugin.Name ?? "Unknown Plugin";
                            string commandText = netCmdSingle ?? "";
                            progress?.Report($"🛠️ Executing {pluginName}...");
                            BricsAI.Core.LoggerService.LogComExecution(pluginName, commandText);
                            string executeResult = plugin.Execute(_acadApp!.ActiveDocument, netCmdSingle);
                            BricsAI.Core.LoggerService.LogComResponse(executeResult);
                            progress?.Report($"✅ {executeResult}\n");
                            return executeResult;
                        }
                        
                        progress?.Report($"⚠️ Unsupported Tool: {netCmdSingle}\n");
                        return $"WARNING Unrecognized or Unsupported NET command: {netCmdSingle}";
                    }
                    else if (!string.IsNullOrEmpty(sLisp) && _acadApp?.ActiveDocument != null)
                    {
                        progress?.Report($"🛠️ Sending Native Command...");
                        BricsAI.Core.LoggerService.LogComExecution("Native LISP", sLisp ?? "");
                        object? ignore = _acadApp!.ActiveDocument.SendCommand(sLisp + "\n");
                        BricsAI.Core.LoggerService.LogComResponse("Command Sent Natively");
                        progress?.Report($"✅ Completed Native String\n");
                        return $"Executed: {sLisp}";
                    }
                    else if (!string.IsNullOrEmpty(sCmd) && _acadApp?.ActiveDocument != null)
                    {
                         progress?.Report($"🛠️ Sending Command {sCmd}...");
                         BricsAI.Core.LoggerService.LogComExecution(sCmd ?? "Command", sCmd ?? "");
                         object? ignore = _acadApp!.ActiveDocument.SendCommand(sCmd + "\n");
                         BricsAI.Core.LoggerService.LogComResponse("Command Sent Natively");
                         progress?.Report($"✅ Completed {sCmd}\n");
                         return $"Executed Command: {sCmd}";
                    }
                }
                return "Error: No valid command found in action.";
            }
            catch (Exception ex)
            {
                return $"Error executing action: {ex.Message}";
            }
        }

    }
}
