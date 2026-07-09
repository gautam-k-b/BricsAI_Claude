using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BricsAI.Core;

namespace BricsAI.McpServer.Services
{
    /// <summary>
    /// Connects to a running BricsCAD (or AutoCAD) instance via COM automation and dispatches
    /// NET: commands to the version-appropriate plugin. Fully synchronous by design — every
    /// method here must only ever be called from inside StaComHost.InvokeAsync, never directly
    /// from an async tool handler.
    /// </summary>
    public class ComClient
    {
        private dynamic? _acadApp;
        private readonly PluginManager _pluginManager = new PluginManager();

        public bool IsConnected => _acadApp != null;
        public int MajorVersion { get; private set; }

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

        public bool Connect()
        {
            if (_acadApp != null) return true;

            try
            {
                _acadApp = GetActiveObject("BricscadApp.AcadApplication");
                if (_acadApp == null)
                {
                    _acadApp = GetActiveObject("AutoCAD.Application");
                }

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
                _acadApp = null;
                return false;
            }
        }

        private void DetectVersion()
        {
            try
            {
                string? versionStr = _acadApp?.Version;
                if (versionStr != null)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(versionStr, @"^\d+");
                    MajorVersion = match.Success && int.TryParse(match.Value, out int v) ? v : 19;
                }
                else
                {
                    MajorVersion = 19;
                }
            }
            catch
            {
                MajorVersion = 19;
            }
        }

        /// <summary>
        /// Unlocks every layer except the protected booth output layers. Mirrors the old
        /// pre-pipeline step that ran before every proofing request.
        /// </summary>
        public void ForceUnlockAllLayersExceptBoothLayers()
        {
            try
            {
                if (_acadApp?.ActiveDocument?.Layers == null) return;

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
            catch { }
        }

        /// <summary>
        /// Runs a single NET: command through the version-appropriate plugin.
        /// </summary>
        public string RunNetCommand(string netCmd)
        {
            if (_acadApp == null && !Connect())
            {
                return "Error: Could not connect to BricsCAD. Is it running?";
            }

            try
            {
                var plugin = _pluginManager.GetPluginForCommand(netCmd, MajorVersion);
                if (plugin == null)
                {
                    return $"WARNING Unrecognized or Unsupported NET command: {netCmd}";
                }

                LoggerService.LogComExecution(plugin.Name ?? "Unknown Plugin", netCmd);
                string result = plugin.Execute(_acadApp!.ActiveDocument, netCmd);
                LoggerService.LogComResponse(result);
                return result;
            }
            catch (Exception ex)
            {
                _acadApp = null;
                return $"Error executing command: {ex.Message}";
            }
        }

        /// <summary>
        /// Sends a raw LISP/native command string directly to BricsCAD's command line.
        /// Escape hatch for anything without a dedicated NET: tool (e.g. (c:a2zcolor), -PURGE).
        /// </summary>
        public string SendRawCommand(string command)
        {
            if (_acadApp == null && !Connect())
            {
                return "Error: Could not connect to BricsCAD. Is it running?";
            }

            try
            {
                LoggerService.LogComExecution("Native LISP", command);
                object? ignore = _acadApp!.ActiveDocument.SendCommand(command + "\n");
                LoggerService.LogComResponse("Command Sent Natively");
                return $"Executed: {command}";
            }
            catch (Exception ex)
            {
                _acadApp = null;
                return $"Error executing command: {ex.Message}";
            }
        }
    }
}
