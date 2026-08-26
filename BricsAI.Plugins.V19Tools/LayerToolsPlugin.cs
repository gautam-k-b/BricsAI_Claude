using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class LayerToolsPlugin : IToolPlugin
    {
        public string Name => "Advanced Layer Manipulations";
        public string Description => "Handles complex dynamic layer mapping, filtering, fast renaming, and deep deletion logic.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Apply the layer mappings from JSON.'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"APPLY_LAYER_MAPPINGS\", \"lisp_code\": \"NET:APPLY_LAYER_MAPPINGS\" }] }\n\n" +
                   "User: 'Delete layers starting with prefix Deleted_'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"DELETE_LAYERS_BY_PREFIX\", \"lisp_code\": \"NET:DELETE_LAYERS_BY_PREFIX:Deleted_\" }] }\n\n" +
                   "User: 'Clean up / erase / delete all objects in layer 0' (or any named layer)\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"ERASE_ENTITIES_ON_LAYER\", \"lisp_code\": \"NET:ERASE_ENTITIES_ON_LAYER:0\" }] }\n" +
                   "(Replace '0' with the actual layer name the user specifies, e.g. NET:ERASE_ENTITIES_ON_LAYER:Expo_Building)\n\n" +
                   "User: 'Select everything on layer X' (optionally 'and move it to layer Y')\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_LAYER\", \"lisp_code\": \"NET:SELECT_LAYER:X\" }] } (or \"NET:SELECT_LAYER:X:Y\" to also move it)\n\n" +
                   "User: 'Select the single largest object on layer X' / 'select everything except the largest on layer X'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_OUTER\", \"lisp_code\": \"NET:SELECT_OUTER:X\" }] } (use NET:SELECT_INNER:X for everything but the largest)\n\n" +
                   "User: 'Unlock all layers starting with prefix Deleted_'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"UNLOCK_LAYERS_BY_PREFIX\", \"lisp_code\": \"NET:UNLOCK_LAYERS_BY_PREFIX:Deleted_\" }] }\n\n" +
                   "User: 'Show me the raw entity data for layer X' (paginated)\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"GET_LAYER_GEOMETRY\", \"lisp_code\": \"NET:GET_LAYER_GEOMETRY:X|0|200\" }] } (offset|maxEntities, e.g. 0|200 for the first 200 entities)\n\n" +
                   "User: 'Export a snapshot image of layer X so I can look at it'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"EXPORT_LAYER_SNAPSHOT\", \"lisp_code\": \"NET:EXPORT_LAYER_SNAPSHOT:X|BMP\" }] } (format is BMP, PNG, or JPG)";
        }

        public bool CanExecute(string netCommandName)
        {
            if (netCommandName == null) return false;
            return netCommandName.StartsWith("NET:SELECT_LAYER") ||
                   netCommandName.StartsWith("NET:SELECT_OUTER") ||
                   netCommandName.StartsWith("NET:SELECT_INNER") ||
                   netCommandName.StartsWith("NET:GET_LAYERS") ||
                   netCommandName.StartsWith("NET:APPLY_LAYER_MAPPINGS") ||
                   netCommandName.StartsWith("NET:RENAME_DELETED_LAYERS") ||
                   netCommandName.StartsWith("NET:DELETE_LAYERS_BY_PREFIX") ||
                   netCommandName.StartsWith("NET:ERASE_ENTITIES_ON_LAYER") ||
                   netCommandName.StartsWith("NET:UNLOCK_LAYERS_BY_PREFIX") ||
                   netCommandName.StartsWith("NET:LOCK_BOOTH_LAYERS") ||
                   netCommandName.StartsWith("NET:POLL_LAYER_SEMANTICS") ||
                   netCommandName.StartsWith("NET:GET_LAYER_GEOMETRY") ||
                   netCommandName.StartsWith("NET:EXPORT_LAYER_SNAPSHOT") ||
                   netCommandName.StartsWith("NET:LEARN_LAYER_MAPPING") ||
                   netCommandName.StartsWith("NET:ISOLATE_LAYERS") ||
                   netCommandName.StartsWith("NET:SHOW_LAYER") ||
                   netCommandName.StartsWith("NET:HIDE_LAYER") ||
                   netCommandName == "NET:SHOW_ALL_LAYERS";
        }

        public string Execute(dynamic doc, string netCmd)
        {
            if (netCmd.StartsWith("NET:SELECT_LAYER:"))
            {
                string arg = netCmd.Substring("NET:SELECT_LAYER:".Length).Trim();
                var layerParts = arg.Split(':');
                string layerName = layerParts[0].Trim();
                string? targetLayer = layerParts.Length > 1 ? layerParts[1].Trim() : null;
                return SelectObjectsOnLayer(doc, layerName, false, "all", targetLayer);
            }
            if (netCmd.StartsWith("NET:SELECT_OUTER:"))
            {
                string layerName = netCmd.Substring("NET:SELECT_OUTER:".Length).Trim();
                return SelectObjectsOnLayer(doc, layerName, false, "outer");
            }
            if (netCmd.StartsWith("NET:SELECT_INNER:"))
            {
                string layerName = netCmd.Substring("NET:SELECT_INNER:".Length).Trim();
                return SelectObjectsOnLayer(doc, layerName, false, "inner");
            }
            if (netCmd.StartsWith("NET:GET_LAYERS")) return GetAllLayers(doc);
            if (netCmd.StartsWith("NET:APPLY_LAYER_MAPPINGS")) return ApplyLayerMappings(doc);
            if (netCmd.StartsWith("NET:RENAME_DELETED_LAYERS")) return RenameDeletedLayers(doc);
            if (netCmd.StartsWith("NET:DELETE_LAYERS_BY_PREFIX:")) return DeleteLayersByPrefix(doc, ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:ERASE_ENTITIES_ON_LAYER:")) return EraseEntitiesOnLayer(doc, netCmd.Substring("NET:ERASE_ENTITIES_ON_LAYER:".Length).Trim());
            if (netCmd.StartsWith("NET:UNLOCK_LAYERS_BY_PREFIX:")) return UnlockLayersByPrefix(doc, ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:LOCK_BOOTH_LAYERS")) return LockBoothLayers(doc);
            if (netCmd.StartsWith("NET:POLL_LAYER_SEMANTICS:")) return PollLayerSemantics(doc, netCmd);
            if (netCmd.StartsWith("NET:GET_LAYER_GEOMETRY:")) return GetLayerGeometry(doc, netCmd);
            if (netCmd.StartsWith("NET:EXPORT_LAYER_SNAPSHOT:")) return ExportLayerSnapshot(doc, netCmd);
            if (netCmd.StartsWith("NET:LEARN_LAYER_MAPPING:")) return LearnLayerMapping(netCmd);
            if (netCmd.StartsWith("NET:ISOLATE_LAYERS:")) return IsolateLayers(doc, netCmd.Substring("NET:ISOLATE_LAYERS:".Length));
            if (netCmd.StartsWith("NET:SHOW_LAYER:")) return SetLayerVisibility(doc, netCmd.Substring("NET:SHOW_LAYER:".Length).Trim(), true);
            if (netCmd.StartsWith("NET:HIDE_LAYER:")) return SetLayerVisibility(doc, netCmd.Substring("NET:HIDE_LAYER:".Length).Trim(), false);
            if (netCmd == "NET:SHOW_ALL_LAYERS") return ShowAllLayers(doc);

            return "Error: Command not explicitly handled in LayerToolsPlugin.";
        }

        private string LearnLayerMapping(string netCmd)
        {
            try
            {
                string mappingData = netCmd.Substring("NET:LEARN_LAYER_MAPPING:".Length).Trim();
                var layerParts = mappingData.Split(':');
                if (layerParts.Length != 2) return "Error: Invalid learn layer mapping format.";
                
                string sourceLayer = layerParts[0].Trim();
                string targetLayer = layerParts[1].Trim();

                var protectedBooths = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                    { "Expo_BoothOutline", "Expo_BoothNumber", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber" };
                if (protectedBooths.Contains(targetLayer))
                    return $"Rejected: '{targetLayer}' is a protected booth output layer and cannot be a mapping target.";

                KnowledgeService.SaveLearning($"Map the layer '{sourceLayer}' to standard layer '{targetLayer}'.");

                return $"Learned mapping natively: {sourceLayer} -> {targetLayer}";
            }
            catch (System.Exception ex)
            {
                return $"Error learning layer mapping: {ex.Message}";
            }
        }

        private string PollLayerSemantics(dynamic doc, string cmd)
        {
            try
            {
                string targetLayer = cmd.Substring("NET:POLL_LAYER_SEMANTICS:".Length).Trim();
                if (string.IsNullOrEmpty(targetLayer)) return "Error: Layer name required.";

                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";
                
                dynamic? sset = null;
                try { sset = selectionSets.Item("BricsAI_PollSet"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_PollSet");

                short[] filterType = new short[] { 8 };
                object[] filterData = new object[] { targetLayer };

                try { sset.Select(5, Type.Missing, Type.Missing, filterType, filterData); } catch { return $"Error: Failed to select layer {targetLayer}"; }

                int count = sset.Count;
                if (count == 0) return "[]";

                Dictionary<string, int> entityTypes = new Dictionary<string, int>();
                HashSet<string> blockNames = new HashSet<string>();
                HashSet<string> textValues = new HashSet<string>();

                for (int i = 0; i < count; i++)
                {
                    var entity = sset.Item(i);
                    string eType = entity.ObjectName;
                    if (entityTypes.ContainsKey(eType)) entityTypes[eType]++; else entityTypes[eType] = 1;

                    if (eType.Contains("BlockReference"))
                    {
                        try { blockNames.Add(entity.Name); } catch { }
                    }
                    else if (eType.Contains("Text") || eType.Contains("MText"))
                    {
                        try 
                        { 
                            string txt = entity.TextString;
                            if (txt.Length > 20) txt = txt.Substring(0, 20);
                            textValues.Add(txt); 
                        } catch { }
                    }
                }

                var result = new
                {
                    Layer = targetLayer,
                    TotalCount = count,
                    EntityTypes = entityTypes,
                    BlockNames = blockNames.ToArray(),
                    TextSample = textValues.ToArray()
                };

                return System.Text.Json.JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                return $"Error polling semantics: {ex.Message}";
            }
        }

        private string GetLayerGeometry(dynamic doc, string cmd)
        {
            try
            {
                var request = ParseGeometryRequest(cmd);
                if (string.IsNullOrEmpty(request.Layer)) return "Error: Layer name required.";
                int pageSize = Math.Clamp(request.MaxEntities, 1, 1000);
                int offset = Math.Max(0, request.Offset);

                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";

                dynamic? sset = null;
                try { sset = selectionSets.Item("BricsAI_GeometrySet"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_GeometrySet");

                short[] filterType = new short[] { 8 };
                object[] filterData = new object[] { request.Layer };

                try { sset.Select(5, Type.Missing, Type.Missing, filterType, filterData); } catch { return $"Error: Failed to select layer {request.Layer}"; }

                int count = sset.Count;
                var entities = new List<object>();
                int endExclusive = Math.Min(count, offset + pageSize);

                for (int i = offset; i < endExclusive; i++)
                {
                    var entity = sset.Item(i);
                    entities.Add(BuildEntitySnapshot(entity));
                }

                var result = new
                {
                    Layer = request.Layer,
                    TotalCount = count,
                    Offset = offset,
                    MaxEntities = pageSize,
                    ReturnedCount = entities.Count,
                    Truncated = endExclusive < count,
                    NextOffset = endExclusive < count ? endExclusive : (int?)null,
                    Entities = entities
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                return $"Error getting layer geometry: {ex.Message}";
            }
        }

        private string ExportLayerSnapshot(dynamic doc, string cmd)
        {
            try
            {
                var request = ParseSnapshotRequest(cmd);
                if (string.IsNullOrWhiteSpace(request.Layer)) return "Error: Layer name required.";

                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";

                dynamic? sset = null;
                try { sset = selectionSets.Item("BricsAI_SnapshotSet"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_SnapshotSet");

                short[] filterType = new short[] { 8 };
                object[] filterData = new object[] { request.Layer };

                try { sset.Select(5, Type.Missing, Type.Missing, filterType, filterData); } catch { return $"Error: Failed to select layer {request.Layer}"; }
                if (sset.Count == 0) return $"Error: Layer '{request.Layer}' has no entities to export.";

                var outDir = Path.Combine(Path.GetTempPath(), "BricsAI", "snapshots");
                Directory.CreateDirectory(outDir);

                string safeLayer = SanitizeFileName(request.Layer);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string basePath = Path.Combine(outDir, $"{timestamp}_{safeLayer}");
                string format = NormalizeFormat(request.Format);

                string filePath;
                try
                {
                    filePath = ExportSelection(doc, sset, basePath, format);
                }
                catch
                {
                    if (!string.Equals(format, "BMP", StringComparison.OrdinalIgnoreCase))
                        filePath = ExportSelection(doc, sset, basePath, "BMP");
                    else
                        throw;
                }

                var result = new
                {
                    Layer = request.Layer,
                    RequestedFormat = request.Format,
                    FormatUsed = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                    EntityCount = (int)sset.Count,
                    FilePath = filePath
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                return $"Error exporting layer snapshot: {ex.Message}";
            }
        }

        private static string ExportSelection(dynamic doc, dynamic sset, string basePath, string format)
        {
            doc.Export(basePath, format, sset);

            string ext = format.ToLowerInvariant();
            string[] candidates = new[]
            {
                basePath,
                basePath + "." + ext,
                basePath + "." + format,
                basePath + "." + format.ToUpperInvariant()
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return basePath + "." + ext;
        }

        private static (string Layer, int Offset, int MaxEntities) ParseGeometryRequest(string cmd)
        {
            string payload = cmd.Substring("NET:GET_LAYER_GEOMETRY:".Length);
            var parts = payload.Split('|');
            string layer = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            int offset = 0;
            int maxEntities = 200;

            if (parts.Length > 1) int.TryParse(parts[1], out offset);
            if (parts.Length > 2 && int.TryParse(parts[2], out var parsedTake)) maxEntities = parsedTake;

            return (layer, offset, maxEntities);
        }

        private static (string Layer, string Format) ParseSnapshotRequest(string cmd)
        {
            string payload = cmd.Substring("NET:EXPORT_LAYER_SNAPSHOT:".Length);
            var parts = payload.Split('|');
            string layer = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            string format = parts.Length > 1 ? parts[1].Trim() : "BMP";
            return (layer, string.IsNullOrWhiteSpace(format) ? "BMP" : format);
        }

        private static string NormalizeFormat(string format)
        {
            var upper = format.Trim().ToUpperInvariant();
            if (upper == "JPG") return "JPEG";
            return string.IsNullOrWhiteSpace(upper) ? "BMP" : upper;
        }

        private static string SanitizeFileName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = input.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static object BuildEntitySnapshot(dynamic entity)
        {
            var entityType = SafeGetString(() => entity.ObjectName) ?? SafeGetString(() => entity.EntityName) ?? "Unknown";
            var dxfType = SafeGetString(() => entity.DxfType);
            var layer = SafeGetString(() => entity.Layer) ?? "";
            var blockName = SafeGetString(() => entity.Name);
            var text = SafeGetString(() => entity.TextString);
            var closed = SafeGetBool(() => entity.Closed);
            var area = SafeGetDouble(() => entity.Area);
            var bounds = GetBounds(entity);
            var coordinates = GetCoordinates(entity);
            var insertionPoint = GetPoint(() => entity.InsertionPoint);
            var startPoint = GetPoint(() => entity.StartPoint);
            var endPoint = GetPoint(() => entity.EndPoint);
            var center = GetPoint(() => entity.Center);
            var radius = SafeGetDouble(() => entity.Radius);

            return new
            {
                EntityType = entityType,
                DxfType = dxfType,
                Layer = layer,
                BlockName = blockName,
                Text = text,
                Closed = closed,
                Area = area,
                Bounds = bounds,
                Coordinates = coordinates,
                InsertionPoint = insertionPoint,
                StartPoint = startPoint,
                EndPoint = endPoint,
                Center = center,
                Radius = radius
            };
        }

        private static object? GetBounds(dynamic entity)
        {
            try
            {
                entity.GetBoundingBox(out object minPt, out object maxPt);
                return new
                {
                    Min = ToDoubleArray(minPt),
                    Max = ToDoubleArray(maxPt)
                };
            }
            catch
            {
                return null;
            }
        }

        private static double[]? GetCoordinates(dynamic entity)
        {
            try { return ToDoubleArray(entity.Coordinates); } catch { return null; }
        }

        private static double[]? GetPoint(Func<object> getter)
        {
            try { return ToDoubleArray(getter()); } catch { return null; }
        }

        private static double[]? ToDoubleArray(object value)
        {
            if (value is Array array)
            {
                var result = new double[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    result[i] = Convert.ToDouble(array.GetValue(i));
                }
                return result;
            }

            return null;
        }

        private static string? SafeGetString(Func<object> getter)
        {
            try
            {
                var value = getter();
                if (value == null) return null;
                var text = Convert.ToString(value);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static bool? SafeGetBool(Func<object> getter)
        {
            try { return Convert.ToBoolean(getter()); } catch { return null; }
        }

        private static double? SafeGetDouble(Func<object> getter)
        {
            try { return Convert.ToDouble(getter()); } catch { return null; }
        }

        private string? ExtractTarget(string cmd)
        {
            var parts = cmd.Split(':');
            return parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null;
        }

        private string SelectObjectsOnLayer(dynamic doc, string layerName, bool exclusive = false, string mode = "all", string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }

                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return $"Error: Could not access SelectionSets.";
                dynamic? sset = null;

                try { sset = selectionSets.Item("BricsAI_SelSet"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_SelSet");

                short[] filterTypes = new short[] { 8 }; // DXF code for Layer
                object[] filterData = new object[] { layerName };

                sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                if (sset.Count > 0)
                {
                    if (mode == "all") 
                    {
                        if (!string.IsNullOrEmpty(targetLayer))
                        {
                            try { 
                                doc.SendCommand("\x03\x03");
                                // doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{layerName}\")))) (command \"_.CHPROP\" ss \"\" \"_LA\" \"{targetLayer}\" \"\"))\n"); 
                                doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{layerName}\")))) (command \"_.CHPROP\" ss \"_LA\" \"{targetLayer}\" \"\"))\n");
                            } catch { }
                            return $"Moved matching objects from '{layerName}' to '{targetLayer}'.";
                        }
                        else
                        {
                            sset.Highlight(true);
                            return $"Selected {sset.Count} objects on layer '{layerName}'. (Highlighted)";
                        }
                    }

                    double maxArea = -1;
                    dynamic? largestObj = null;
                    var smallerObjs = new List<dynamic>();

                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            obj.GetBoundingBox(out object minPt, out object maxPt);
                            double[] min = (double[])minPt;
                            double[] max = (double[])maxPt;
                            double width = Math.Abs(max[0] - min[0]);
                            double height = Math.Abs(max[1] - min[1]);
                            double area = width * height;

                            if (area > maxArea)
                            {
                                if (largestObj != null) smallerObjs.Add(largestObj);
                                maxArea = area;
                                largestObj = obj;
                            }
                            else
                            {
                                smallerObjs.Add(obj);
                            }
                        }
                        catch { }
                    }

                    sset.Highlight(false); 

                    if (mode == "outer" && largestObj != null)
                    {
                        largestObj!.Highlight(true);
                        return $"Selected outer box (largest bounds) on layer '{layerName}'.";
                    }
                    else if (mode == "inner" && smallerObjs.Count > 0)
                    {
                        foreach (var innerObj in smallerObjs)
                        {
                            innerObj.Highlight(true);
                        }
                        return $"Selected {smallerObjs.Count} inner objects on layer '{layerName}'.";
                    }
                }
                
                return $"No objects found on layer '{layerName}'.";
            }
            catch (Exception ex)
            {
                return $"Error selecting layer: {ex.Message}";
            }
        }

        // private string ApplyLayerMappings(dynamic doc)
        // {
        //     try
        //     {                
        //         var mappings = KnowledgeService.GetLayerMappingsDictionary();
        //         if (mappings == null || mappings.Count == 0) return "Error: No layer mappings strictly learned yet.";

        //         System.Text.StringBuilder lispMacro = new System.Text.StringBuilder();

        //         foreach (var kvp in mappings)
        //         {
        //             try { doc!.Layers.Add(kvp.Value); } catch { } // Ensure target exists
        //             // lispMacro.AppendLine($"(if (setq ss (ssget \"_X\" '((8 . \"{kvp.Key}\")))) (command \"_.CHPROP\" ss \"\" \"_LA\" \"{kvp.Value}\" \"\"))");
        //             lispMacro.AppendLine($"(if (setq ss (ssget \"_X\" '((8 . \"{kvp.Key}\")))) (command \"_.CHPROP\" ss \"_LA\" \"{kvp.Value}\" \"\"))");
        //         }

        //         if (lispMacro.Length > 0)
        //         {
        //             lispMacro.AppendLine("(princ)"); // suppress (load) return value — prevents help.bricsys.com from opening
        //             string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "_BricsAI_Mappings.lsp");
        //             System.IO.File.WriteAllText(tempPath, lispMacro.ToString());
        //             string lispPath = tempPath.Replace("\\", "/");
        //             doc.SendCommand("\x03\x03"); // Clear execution line
        //             doc.SendCommand($"(load \"{lispPath}\")\n");        
        //         }

        //         return $"Applied mappings natively with a single batch execution via LISP script load.";
        //     }
        //     catch (Exception ex)
        //     {
        //         return $"Error applying mappings: {ex.Message}";
        //     }
        // }
        
        private string ApplyLayerMappings(dynamic doc)
        {
            try
            {
                var mappings = KnowledgeService.GetLayerMappingsDictionary();
                if (mappings == null || mappings.Count == 0)
                    return "Error: No layer mappings strictly learned yet.";

                LoggerService.LogTransaction("PLUGIN", $"ApplyLayerMappings: starting with {mappings.Count} mapping rules.");
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Booth layers that must NEVER be unlocked
                var boothLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Expo_BoothOutline",
                    "Expo_BoothNumber",
                    "Expo_MaxBoothOutline",
                    "Expo_MaxBoothNumber"
                };

                // Step 1: Ensure all target layers exist and pre-unlock all source layers (one COM call per layer, not per entity)
                // BUT NEVER unlock booth layers — they must remain locked throughout the entire proofing sequence
                foreach (var kvp in mappings)
                {
                    try { doc.Layers.Add(kvp.Value); } catch { }
                    // Only unlock if it's NOT a booth layer
                    if (!boothLayers.Contains(kvp.Key))
                    {
                        try { doc.Layers.Item(kvp.Key).Lock = false; } catch { }
                    }
                }

                // Step 2: Batch remap via LISP ssget+CHPROP — one native command per mapping rule.
                // This is orders of magnitude faster than iterating every entity via COM.
                int ruleCount = 0;
                doc.SendCommand("\x03\x03");
                foreach (var kvp in mappings)
                {
                    string src = kvp.Key;
                    string tgt = kvp.Value;

                    if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase)) continue;
                    // Booth output layers are write-protected — never remap any vendor layer onto them.
                    if (boothLayers.Contains(tgt)) continue;

                    // Escape quotes for LISP string literals
                    string escapedSrc = src.Replace("\"", "\\\"");
                    string escapedTgt = tgt.Replace("\"", "\\\"");

                    string lispCmd = $"(if (setq ss (ssget \"_X\" '((8 . \"{escapedSrc}\")))) (command \"_.CHPROP\" ss \"\" \"_LA\" \"{escapedTgt}\" \"\"))\n";
                    LoggerService.LogTransaction("PLUGIN", $"ApplyLayerMappings: remapping '{src}' -> '{tgt}'.");
                    doc.SendCommand(lispCmd);
                    ruleCount++;
                }

                sw.Stop();
                LoggerService.LogTransaction("PLUGIN", $"ApplyLayerMappings: sent {ruleCount} remap commands in {sw.ElapsedMilliseconds}ms.");
                return $"Applied {ruleCount} layer mapping rules via native LISP batch.";
            }
            catch (Exception ex)
            {
                return $"Error applying mappings: {ex.Message}";
            }
        }

        private string RenameDeletedLayers(dynamic doc)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                
                var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "0", "Defpoints", 
                    "Expo_BoothNumber", "Expo_BoothOutline", 
                    "Expo_MaxBoothNumber", "Expo_MaxBoothOutline", 
                    "Expo_Building", "Expo_Column", 
                    "Expo_Markings", "Expo_NES", "Expo_View2"
                };

                int renameCount = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers.Item(i);
                    string name = layer.Name;

                    try { if ((bool)layer.Freeze) continue; } catch { } // frozen layers are left untouched, not retired

                    if (!allowList.Contains(name) && !name.StartsWith("Deleted_", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            layer.Name = "Deleted_" + name;
                            layer.Lock = false; // Reset lock state when relegating to deleted
                            renameCount++;
                        }
                        catch { }
                    }
                }
                return $"Found and renamed {renameCount} non-standard layers with 'Deleted_' prefix.";
            }
            catch (Exception ex)
            {
                return $"Error renaming layers: {ex.Message}";
            }
        }

        private string EraseEntitiesOnLayer(dynamic doc, string layerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(layerName)) return "Error: No layer name provided.";
                string safe = layerName.Replace("\"", "\\\"");

                // Unlock/thaw the layer first so entities are selectable
                var layers = doc?.Layers;
                if (layers != null)
                {
                    for (int i = 0; i < layers.Count; i++)
                    {
                        try
                        {
                            var l = layers.Item(i);
                            if (((string)l.Name).Equals(layerName, StringComparison.OrdinalIgnoreCase))
                            {
                                l.Lock = false;
                                l.Freeze = false;
                                l.LayerOn = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // Cancel any active command, then erase all entities on the layer via ssget
                doc.SendCommand("\x03\x03");
                doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{safe}\")))) (command \"_.ERASE\" ss \"\"))\n");
                System.Threading.Thread.Sleep(400); // Wait for BricsCAD to finish processing the erase

                // ssget "_X" handles all model-space and layout entities.
                // COM pass covers only *Paper_Space* layouts (viewport objects, etc.) —
                // named block definitions are intentionally excluded: entities on layer 0
                // inside block defs are the block's own geometry; deleting them destroys
                // all inserts of that block (e.g. booth outlines).
                var blocks = doc.Blocks;
                int erased = 0;
                for (int i = 0; i < blocks.Count; i++)
                {
                    string blkName = "";
                    try { blkName = (string)blocks.Item(i).Name; } catch { continue; }
                    // Only paper space layouts — never named block definitions
                    if (!blkName.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)) continue;
                    var blk = blocks.Item(i);
                    try { if (blk.IsXRef) continue; } catch { }
                    for (int j = blk.Count - 1; j >= 0; j--)
                    {
                        try
                        {
                            var ent = blk.Item(j);
                            if (((string)ent.Layer).Equals(layerName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { ent.Delete(); erased++; }
                                catch { ent.Layer = "0"; erased++; }
                            }
                        }
                        catch { }
                    }
                }

                LoggerService.LogTransaction("PLUGIN", $"EraseEntitiesOnLayer: erased entities on layer '{layerName}' (COM pass: {erased} objects moved/deleted).");
                return $"Erased all entities on layer '{layerName}' (ssget + COM pass).";
            }
            catch (Exception ex)
            {
                return $"Error erasing entities on layer '{layerName}': {ex.Message}";
            }
        }

        private string DeleteLayersByPrefix(dynamic doc, string prefix)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";

                // 1. Switch active layer to 0 (cannot purge the current layer)
                doc.SendCommand("(setvar \"CLAYER\" \"0\")\n");
                int targetLayerCount = 0;
                int unlockedCount = 0;
                
                // 2. Strip all protections via COM (fast — only property sets, no entity traversal)
                for (int i = 0; i < layers.Count; i++)
                {
                    try
                    {
                        var layer = layers.Item(i);
                        string name = layer.Name;

                        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            targetLayerCount++;
                            layer.Lock = false;
                            layer.Freeze = false;
                            layer.LayerOn = true;
                            unlockedCount++;
                        }
                    }
                    catch { }
                }

                if (targetLayerCount == 0)
                {
                    return $"Found 0 layers starting with '{prefix}'. No deletion necessary.";
                }

                // 3. Build a layer-name filter list for LISP ssget — moves ALL entities on ALL
                // matching layers to layer "0" via CHPROP (not ERASE). This handles viewport border
                // objects (AcDbViewport) which BricsCAD protects from ERASE but accepts CHPROP on.
                // Once entities are on layer 0, the Deleted_ layers become truly empty.
                LoggerService.LogTransaction("PLUGIN", $"DeleteLayersByPrefix: building layer filter for {targetLayerCount} '{prefix}*' layers.");

                var matchedLayerNames = new System.Collections.Generic.List<string>();
                for (int i = 0; i < layers.Count; i++)
                {
                    try
                    {
                        string lname = (string)layers.Item(i).Name;
                        if (lname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            matchedLayerNames.Add(lname);
                    }
                    catch { }
                }

                // Use CHPROP to move all entities (including viewport objects) to layer "0".
                // This empties the Deleted_ layers without deleting any geometry.
                doc.SendCommand("\x03\x03");
                string escapedPrefix = prefix.Replace("\"", "\\\"");
                if (matchedLayerNames.Count == 1)
                {
                    string ln = matchedLayerNames[0].Replace("\"", "\\\"");
                    doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{ln}\")))) (command \"_.CHPROP\" ss \"\" \"LA\" \"0\" \"\"))\n");
                }
                else
                {
                    var orParts = string.Join(" ", matchedLayerNames.Select(ln => $"(8 . \"{ln.Replace("\"", "\\\"")}\")" ));
                    doc.SendCommand($"(if (setq ss (ssget \"_X\" '((-4 . \"<OR\") {orParts} (-4 . \"OR>\"))))  (command \"_.CHPROP\" ss \"\" \"LA\" \"0\" \"\"))\n");
                }

                // Pass 2: COM traversal of ALL block definitions including *Paper_Space* layouts.
                // Viewport border entities (AcDbViewport) live in *Paper_Space* — we must include them.
                // Move entities to layer "0" rather than deleting (same safe approach as above).
                var blocks = doc.Blocks;
                for (int i = 0; i < blocks.Count; i++)
                {
                    string blkName = "";
                    try { blkName = (string)blocks.Item(i).Name; } catch { continue; }
                    if (blkName.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase)) continue; // ssget handled model space
                    var blk = blocks.Item(i);
                    try { if (blk.IsXRef) continue; } catch { }
                    for (int j = blk.Count - 1; j >= 0; j--)
                    {
                        try
                        {
                            var ent = blk.Item(j);
                            if (((string)ent.Layer).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                ent.Layer = "0"; // move to layer 0 instead of delete
                        }
                        catch { }
                    }
                }

                // 4. Force-delete each matched layer via -LAYDEL (handles viewport overrides and any
                //    other table references that -PURGE silently skips). One call per layer, guarded
                //    by tblsearch so we don't error on already-deleted layers.
                foreach (var ln in matchedLayerNames)
                {
                    string safeLn = ln.Replace("\"", "\\\"");
                    doc.SendCommand($"(if (tblsearch \"LAYER\" \"{safeLn}\") (command \"-LAYDEL\" \"{safeLn}\" \"Y\"))\n");
                }

                // 5. Final PURGE for anything remaining
                doc.SendCommand($"(progn (command \"-PURGE\" \"LA\" \"{escapedPrefix}*\" \"N\") (command \"-PURGE\" \"All\" \"*\" \"N\"))\n");
                LoggerService.LogTransaction("PLUGIN", $"DeleteLayersByPrefix: erased all entities on {matchedLayerNames.Count} layers and purged.");

                return $"Unlocked {unlockedCount} '{prefix}' layers, erased all entities via batch LISP ssget, force-deleted via LAYDEL, and purged empty layers.";
            }
            catch (Exception ex)
            {
                return $"Error deleting layers by prefix: {ex.Message}";
            }
        }

        private string UnlockLayersByPrefix(dynamic doc, string? prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return "Error: Prefix required.";
            try
            {
                // Booth layers that must NEVER be unlocked
                var boothLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Expo_BoothOutline",
                    "Expo_BoothNumber",
                    "Expo_MaxBoothOutline",
                    "Expo_MaxBoothNumber"
                };

                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";

                int unlockedCount = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers.Item(i);
                    string lName = layer.Name;

                    if (lName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !boothLayers.Contains(lName))
                    {
                        try
                        {
                            if ((bool)layer.Freeze) continue; // frozen layers are left untouched
                            layer.Lock = false;
                            unlockedCount++;
                        }
                        catch { }
                    }
                }
                
                return $"Successfully unlocked {unlockedCount} layers starting with '{prefix}' (booth layers protected).".TrimEnd(')');
            }
            catch (Exception ex)
            {
                return $"Error unlocking layers: {ex.Message}";
            }
        }

        private string LockBoothLayers(dynamic doc)
        {
            try
            {
                try { doc.Layers.Item("Expo_BoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_BoothOutline").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothOutline").Lock = true; } catch { }
                return "Locked Expo_BoothNumber, Expo_BoothOutline, Expo_MaxBoothNumber, and Expo_MaxBoothOutline via COM.";
            }
            catch (Exception ex)
            {
                return $"Error locking layers: {ex.Message}";
            }
        }

        private string GetAllLayers(dynamic doc)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                
                var layerNames = new List<string>();
                int frozenSkipped = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers.Item(i);
                    try { if ((bool)layer.Freeze) { frozenSkipped++; continue; } } catch { }
                    layerNames.Add((string)layer.Name);
                }

                if (frozenSkipped > 0)
                    LoggerService.LogTransaction("PLUGIN", $"GetAllLayers: skipped {frozenSkipped} frozen layer(s).");

                return $"Layers found: {string.Join(", ", layerNames)}";
            }
            catch (Exception ex)
            {
                return $"Error getting layers: {ex.Message}";
            }
        }

        /// <summary>
        /// Hides ALL layers except the listed ones (comma-separated). Layer '0' is always kept visible.
        /// </summary>
        private string IsolateLayers(dynamic doc, string layerList)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";

                var keepVisible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                keepVisible.Add("0"); // Layer 0 always stays visible
                foreach (var l in layerList.Split(','))
                {
                    string trimmed = l.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        keepVisible.Add(trimmed);
                }

                int hiddenCount = 0;
                int shownCount = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    try
                    {
                        var layer = layers.Item(i);
                        string name = (string)layer.Name;
                        bool shouldBeVisible = keepVisible.Contains(name);
                        layer.LayerOn = shouldBeVisible;
                        if (shouldBeVisible) shownCount++; else hiddenCount++;
                    }
                    catch { }
                }

                return $"Isolated layers. Showing {shownCount} layers ({string.Join(", ", keepVisible)}), hidden {hiddenCount} others.";
            }
            catch (Exception ex)
            {
                return $"Error isolating layers: {ex.Message}";
            }
        }

        /// <summary>
        /// Turns a single layer on or off by name.
        /// </summary>
        private string SetLayerVisibility(dynamic doc, string layerName, bool visible)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                dynamic layer = layers.Item(layerName);
                layer.LayerOn = visible;
                return $"Layer '{layerName}' is now {(visible ? "visible" : "hidden")}.";
            }
            catch (Exception ex)
            {
                return $"Error setting layer visibility: {ex.Message}";
            }
        }

        /// <summary>
        /// Makes all layers visible.
        /// </summary>
        private string ShowAllLayers(dynamic doc)
        {
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return "Error: Could not access Layers.";
                int count = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    try { layers.Item(i).LayerOn = true; count++; } catch { }
                }
                return $"Made all {count} layers visible.";
            }
            catch (Exception ex)
            {
                return $"Error showing all layers: {ex.Message}";
            }
        }
    }
}
