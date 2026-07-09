using System;
using System.Collections.Generic;
using BricsAI.Core;

namespace BricsAI.Plugins.V19Tools
{
    public class GeometryToolsPlugin : IToolPlugin
    {
        public string Name => "Geometric Feature Selection & Preparation";
        public string Description => "Handles complex geometry evaluation like bounding boxes, columns, utilities, and whitelist explosions.";
        public int TargetVersion => 19;

        public string GetPromptExample()
        {
            return "User: 'Select the booth outlines'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_BOOTH_BOXES\", \"lisp_code\": \"NET:SELECT_BOOTH_BOXES:Expo_BoothOutline\" }] }\n\n" +
                   "User: 'Move empty or unnumbered booths to the trash layer'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_EMPTY_BOOTHS\", \"lisp_code\": \"NET:SELECT_EMPTY_BOOTHS:TrashLayer\" }] }\n\n" +
                   "User: 'Count booths without a booth number / how many booth outlines have no number / investigate and give count of unmatched booths'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"COUNT_EMPTY_BOOTHS\", \"lisp_code\": \"NET:COUNT_EMPTY_BOOTHS\" }] }\n\n" +
                   "User: 'Prepare the geometry'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"PREPARE_GEOMETRY\", \"lisp_code\": \"NET:PREPARE_GEOMETRY\" }] }";
        }

        public bool CanExecute(string netCommandName)
        {
            if (netCommandName == null) return false;
            return netCommandName.StartsWith("NET:SELECT_BOOTH_BOXES") ||
                   netCommandName.StartsWith("NET:SELECT_EMPTY_BOOTHS") ||
                   netCommandName.StartsWith("NET:COUNT_EMPTY_BOOTHS") ||
                   netCommandName.StartsWith("NET:SELECT_BUILDING_LINES") ||
                   netCommandName.StartsWith("NET:SELECT_COLUMNS") ||
                   netCommandName.StartsWith("NET:SELECT_UTILITIES") ||
                   netCommandName.StartsWith("NET:PREPARE_GEOMETRY");
        }

        public string Execute(dynamic doc, string netCmd)
        {
            if (netCmd.StartsWith("NET:SELECT_BOOTH_BOXES")) return SelectGeometricFeatures(doc, "booths", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_EMPTY_BOOTHS")) return SelectEmptyBooths(doc, ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:COUNT_EMPTY_BOOTHS")) return CountEmptyBooths(doc);
            if (netCmd.StartsWith("NET:SELECT_BUILDING_LINES")) return SelectGeometricFeatures(doc, "building", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_COLUMNS")) return SelectGeometricFeatures(doc, "columns", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:SELECT_UTILITIES")) return SelectGeometricFeatures(doc, "utilities", ExtractTarget(netCmd));
            if (netCmd.StartsWith("NET:PREPARE_GEOMETRY")) return PrepareGeometry(doc);

            return "Error: Command not explicitly handled in GeometryToolsPlugin.";
        }

        private string? ExtractTarget(string cmd)
        {
            var parts = cmd.Split(':');
            return parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null;
        }

        private void SendCommandSafe(dynamic doc, string? command)
        {
            try
            {
                if (doc != null && !string.IsNullOrEmpty(command))
                {
                    doc!.SendCommand(command);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendCommand failed: {ex.Message}");
            }
        }

        private string CountEmptyBooths(dynamic doc)
        {
            try
            {
                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";

                dynamic? ssetBooths = null;
                try { ssetBooths = selectionSets.Item("BA_CbBooths"); ssetBooths.Delete(); } catch { }
                ssetBooths = selectionSets.Add("BA_CbBooths");

                short[] bTypes = new short[] { 8 };
                object[] bData = new object[] { "Expo_BoothOutline" };
                ssetBooths.Select(5, Type.Missing, Type.Missing, bTypes, bData);

                dynamic? ssetText = null;
                try { ssetText = selectionSets.Item("BA_CbTexts"); ssetText.Delete(); } catch { }
                ssetText = selectionSets.Add("BA_CbTexts");

                short[] tTypes = new short[] { 0, 8 };
                object[] tData = new object[] { "TEXT,MTEXT", "Expo_BoothNumber" };
                ssetText.Select(5, Type.Missing, Type.Missing, tTypes, tData);

                var textPoints = new List<double[]>();
                for (int i = 0; i < ssetText.Count; i++)
                {
                    try
                    {
                        var txt = ssetText.Item(i);
                        double[] pt = (double[])txt.InsertionPoint;
                        textPoints.Add(pt);
                    }
                    catch { }
                }

                int totalOutlines = ssetBooths.Count;
                int unmatchedCount = 0;

                for (int i = 0; i < ssetBooths.Count; i++)
                {
                    try
                    {
                        var obj = ssetBooths.Item(i);
                        object coordsObj = obj.Coordinates;
                        double[] coords = (double[])coordsObj;
                        var vertices = new List<double[]>();

                        if (obj.ObjectName == "AcDbPolyline")
                        {
                            for (int v = 0; v < coords.Length; v += 2)
                                vertices.Add(new double[] { coords[v], coords[v + 1] });
                        }
                        else
                        {
                            for (int v = 0; v < coords.Length; v += 3)
                                vertices.Add(new double[] { coords[v], coords[v + 1] });
                        }

                        bool hasText = textPoints.Exists(pt => IsPointInPolygon(pt[0], pt[1], vertices));
                        if (!hasText) unmatchedCount++;
                    }
                    catch { }
                }

                try { ssetBooths.Delete(); } catch { }
                try { ssetText.Delete(); } catch { }

                return $"Booth outline count: {totalOutlines}. Booth number count: {ssetText.Count}. " +
                       $"Booths WITHOUT a booth number: {unmatchedCount}. " +
                       $"Booths WITH a booth number: {totalOutlines - unmatchedCount}.";
            }
            catch (Exception ex)
            {
                return $"Error counting empty booths: {ex.Message}";
            }
        }

        private string SelectEmptyBooths(dynamic doc, string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }
                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";

                dynamic? ssetBooths = null;
                try { ssetBooths = selectionSets.Item("BA_EbBooths"); ssetBooths.Delete(); } catch { }
                ssetBooths = selectionSets.Add("BA_EbBooths");

                short[] bTypes = new short[] { 8 };
                object[] bData = new object[] { "Expo_BoothOutline" };
                ssetBooths.Select(5, Type.Missing, Type.Missing, bTypes, bData);

                dynamic? ssetText = null;
                try { ssetText = selectionSets.Item("BA_EbTexts"); ssetText.Delete(); } catch { }
                ssetText = selectionSets.Add("BA_EbTexts");

                short[] tTypes = new short[] { 0, 8 };
                object[] tData = new object[] { "TEXT,MTEXT", "Expo_BoothNumber" };
                ssetText.Select(5, Type.Missing, Type.Missing, tTypes, tData);

                var textPoints = new System.Collections.Generic.List<double[]>();
                for (int i = 0; i < ssetText.Count; i++)
                {
                    try
                    {
                        var txt = ssetText.Item(i);
                        double[] pt = (double[])txt.InsertionPoint;
                        textPoints.Add(pt);
                    }
                    catch { }
                }

                var emptyBooths = new System.Collections.Generic.List<dynamic>();
                string debugInfo = $"BA_EbBooths found: {ssetBooths.Count}, BA_EbTexts found: {ssetText.Count}. ";

                for (int i = 0; i < ssetBooths.Count; i++)
                {
                    try
                    {
                        var obj = ssetBooths.Item(i);

                        // Extract Polyline Vertices
                        object coordsObj = obj.Coordinates;
                        double[] coords = (double[])coordsObj;
                        var vertices = new System.Collections.Generic.List<double[]>();

                        // LWPOLYLINE coordinates are flat arrays of [X, Y, X, Y...] (2D)
                        if (obj.ObjectName == "AcDbPolyline")
                        {
                            for (int v = 0; v < coords.Length; v += 2)
                            {
                                vertices.Add(new double[] { coords[v], coords[v + 1] });
                            }
                        }
                        else // Legacy 3D polyline [X,Y,Z, X,Y,Z...]
                        {
                            for (int v = 0; v < coords.Length; v += 3)
                            {
                                vertices.Add(new double[] { coords[v], coords[v + 1] });
                            }
                        }

                        bool hasText = false;
                        foreach (var pt in textPoints)
                        {
                            if (IsPointInPolygon(pt[0], pt[1], vertices))
                            {
                                hasText = true;
                                break;
                            }
                        }

                        if (!hasText)
                        {
                            emptyBooths.Add(obj);
                            if (!string.IsNullOrEmpty(targetLayer))
                            {
                                try { obj.Layer = targetLayer; } catch { }
                            }
                            else
                            {
                                try { obj.Highlight(true); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                try { ssetBooths.Delete(); } catch { }
                try { ssetText.Delete(); } catch { }

                if (emptyBooths.Count > 0)
                {
                    return $"{debugInfo}Selected {emptyBooths.Count} unnumbered booths." + (!string.IsNullOrEmpty(targetLayer) ? $" -> Moved to {targetLayer}" : "");
                }
                return $"{debugInfo}No unnumbered booths found.";
            }
            catch (Exception ex)
            {
                return $"Error selecting empty booths: {ex.Message}";
            }
        }

        // Ray-Casting algorithm to determine if a point is inside a polygon
        private bool IsPointInPolygon(double tx, double ty, System.Collections.Generic.List<double[]> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                double xi = polygon[i][0], yi = polygon[i][1];
                double xj = polygon[j][0], yj = polygon[j][1];

                bool intersect = ((yi > ty) != (yj > ty))
                    && (tx < (xj - xi) * (ty - yi) / (yj - yi) + xi);
                if (intersect) inside = !inside;
                j = i;
            }
            return inside;
        }

        private string SelectGeometricFeatures(dynamic doc, string featureType, string? targetLayer = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetLayer)) { try { doc!.Layers.Add(targetLayer); } catch { } }
                var selectionSets = doc?.SelectionSets;
                if (selectionSets == null) return "Error: Could not access SelectionSets.";
                dynamic? sset = null;
                try { sset = selectionSets.Item("BricsAI_GeoSel"); sset.Delete(); } catch { }
                sset = selectionSets.Add("BricsAI_GeoSel");

                if (featureType == "booths")
                {
                    short[] filterTypes = new short[] { 0 };
                    object[] filterData = new object[] { "LWPOLYLINE,POLYLINE" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                    var validObjs = new List<dynamic>();
                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        try
                        {
                            if (obj.Closed)
                            {
                                double area = obj.Area;
                                if (area >= 90 && area <= 150)
                                {
                                    validObjs.Add(obj);
                                    if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                                    else { obj.Highlight(true); }
                                }
                            }
                        }
                        catch { }
                    }
                    if (validObjs.Count > 0)
                    {
                        try { doc!.SendCommand("PICKFIRST 1\n"); } catch { }
                        return $"Selected {validObjs.Count} booth boxes.";
                    }
                    return "No booth boxes found.";
                }
                else if (featureType == "building")
                {
                    short[] filterTypes = new short[] { 0 };
                    object[] filterData = new object[] { "LWPOLYLINE,POLYLINE" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                    double maxArea = -1;
                    dynamic? largestObj = null;

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
                                maxArea = area;
                                largestObj = obj;
                            }
                        }
                        catch { }
                    }

                    if (largestObj != null)
                    {
                        if (!string.IsNullOrEmpty(targetLayer)) { try { largestObj.Layer = targetLayer; } catch { } }
                        else { largestObj.Highlight(true); }
                        return $"Selected outer building outline." + (!string.IsNullOrEmpty(targetLayer) ? $" -> Moved to {targetLayer}" : "");
                    }
                    return "No building outline found.";
                }
                else if (featureType == "columns")
                {
                    short[] filterTypes = new short[] { 0 };
                    object[] filterData = new object[] { "CIRCLE,INSERT" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                    var validObjs = new List<dynamic>();
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

                            if (area > 0 && area < 50)
                            {
                                validObjs.Add(obj);
                                if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                                else { obj.Highlight(true); }
                            }
                        }
                        catch { }
                    }

                    if (validObjs.Count > 0)
                    {
                        return $"Selected {validObjs.Count} columns.";
                    }
                    return "No columns found.";
                }
                else if (featureType == "utilities")
                {
                    short[] filterTypes = new short[] { 0 };
                    object[] filterData = new object[] { "HATCH" };
                    sset.Select(5, Type.Missing, Type.Missing, filterTypes, filterData);

                    int count = 0;
                    for (int i = 0; i < sset.Count; i++)
                    {
                        var obj = sset.Item(i);
                        if (!string.IsNullOrEmpty(targetLayer)) { try { obj.Layer = targetLayer; } catch { } }
                        else { obj.Highlight(true); }
                        count++;
                    }

                    if (count > 0)
                    {
                        return $"Selected {count} utility hatches/symbols.";
                    }
                    return "No utilities found.";
                }

                return "Unknown geometric feature type.";
            }
            catch (Exception ex)
            {
                return $"Error selecting geometric features: {ex.Message}";
            }
        }

        private string PrepareGeometry(dynamic doc)
        {
            try
            {
                // ─── Suppress display overhead for the entire operation ───
                SendCommandSafe(doc, "(setvar \"REGENMODE\" 0)\n");
                SendCommandSafe(doc, "(setvar \"CMDECHO\" 0)\n");

                LoggerService.LogTransaction("PLUGIN", "PrepareGeometry: locking booth target layers.");
                // 1. Lock booth layers natively (targets)
                try { doc.Layers.Item("Expo_BoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_BoothOutline").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothOutline").Lock = true; } catch { }

                // 1b. Lock mapped vendor sources
                try
                {
                    var mappings = KnowledgeService.GetLayerMappingsDictionary();
                    if (mappings != null && mappings.Count > 0)
                    {
                        foreach (var kvp in mappings)
                        {
                            string target = kvp.Value.Trim();
                            if (target.Equals("Expo_BoothOutline", StringComparison.OrdinalIgnoreCase) ||
                                target.Equals("Expo_BoothNumber", StringComparison.OrdinalIgnoreCase) ||
                                target.Equals("Expo_MaxBoothOutline", StringComparison.OrdinalIgnoreCase) ||
                                target.Equals("Expo_MaxBoothNumber", StringComparison.OrdinalIgnoreCase))
                            {
                                try { doc.Layers.Item(kvp.Key.Trim()).Lock = true; } catch { }
                            }
                        }
                    }
                }
                catch { }

                // 1c. Heuristic fallback: lock unmapped booth-like layers
                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    try
                    {
                        var lyr = doc.Layers.Item(i);
                        string nName = ((string)lyr.Name).ToLower()
                            .Replace(" ", "").Replace("_", "").Replace("-", "");
                        if (nName.Contains("boothoutline") || nName.Contains("boothnumber") ||
                            nName.Contains("maxboothoutline") || nName.Contains("maxboothnumber"))
                        {
                            lyr.Lock = true;
                        }
                    }
                    catch { }
                }

                SendCommandSafe(doc, "(setvar \"PICKFIRST\" 1)\n");

                // 2a. Flatten splines
                try
                {
                    string sName = "BA_Spline_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                    var ssetSplines = doc.SelectionSets.Add(sName);
                    ssetSplines.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { "SPLINE" });
                    if (ssetSplines.Count > 0)
                    {
                        SendCommandSafe(doc, "\x03\x03");
                        SendCommandSafe(doc, "(if (setq ss (ssget \"_X\" '((0 . \"SPLINE\")))) (sssetfirst nil ss))\n");
                        SendCommandSafe(doc, "FLATTEN\n\n\n");
                    }
                    try { ssetSplines.Delete(); } catch { }
                }
                catch { }

                SendCommandSafe(doc, "(setvar \"QATOL\" 0.001)\n");

                // 2b. Global Exhaustive Explosion — REUSE one SelectionSet across all passes
                string whitelistFilter = "'((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\"))";
                short[] wType = new short[] { -4, -4, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
                object[] wData = new object[] { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "OR>", "NOT>" };

                // FIX: Create ONE reusable SelectionSet outside the loop
                string reusableName = "BA_GlobalExp_Reuse";
                dynamic? ssetReuse = null;
                try { ssetReuse = doc.SelectionSets.Item(reusableName); ssetReuse.Delete(); } catch { }
                ssetReuse = doc.SelectionSets.Add(reusableName);

                int maxPasses = 30;
                int passCount = 0;
                int previousNonStandardCount = -1;
                int identicalCountLoops = 0;
                var geometryStopwatch = System.Diagnostics.Stopwatch.StartNew();

                LoggerService.LogTransaction("PLUGIN", "PrepareGeometry: geometry explode loop started.");
                while (passCount < maxPasses)
                {
                    passCount++;
                    int currentNonStandardCount = 0;
                    var passStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        // FIX: Clear and reselect instead of Add/Delete every pass
                        ssetReuse.Clear();
                        ssetReuse.Select(5, Type.Missing, Type.Missing, wType, wData);
                        currentNonStandardCount = ssetReuse.Count;

                        LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: pass {passCount} start (non-standard: {currentNonStandardCount}, previous: {previousNonStandardCount}, identicalLoops: {identicalCountLoops}).");

                        if (currentNonStandardCount > 0)
                        {
                            if (currentNonStandardCount == previousNonStandardCount)
                            {
                                identicalCountLoops++;
                                if (identicalCountLoops >= 2)
                                {
                                    LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: pass {passCount} breaking due to identical non-standard count >= 2.");
                                    break; // Unexplodable remainder
                                }
                            }
                            else
                            {
                                identicalCountLoops = 0;
                            }

                            previousNonStandardCount = currentNonStandardCount;

                            SendCommandSafe(doc, "\x03\x03");
                            SendCommandSafe(doc, $"(if (setq ss (ssget \"_X\" {whitelistFilter})) (command \"_.EXPLODE\" ss \"\"))\n");

                            // FIX: Adaptive sleep — scale with entity count, capped between 200ms–800ms
                            int adaptiveSleep = Math.Min(800, Math.Max(200, currentNonStandardCount / 5));
                            System.Threading.Thread.Sleep(adaptiveSleep);
                        }
                        else
                        {
                            LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: pass {passCount} found no non-standard objects; exiting.");
                            break; // Perfect geometry achieved
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: pass {passCount} caught exception: {ex.Message}");
                        break;
                    }
                    finally
                    {
                        passStopwatch.Stop();
                        LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: pass {passCount} duration {passStopwatch.ElapsedMilliseconds}ms.");
                    }

                    if (geometryStopwatch.Elapsed.TotalSeconds > 120)
                    {
                        LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: elapsed {geometryStopwatch.Elapsed.TotalSeconds:F1}s exceeded 120s, aborting.");
                        break;
                    }
                }
                LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: geometry explode loop ended after {passCount} passes, elapsed {geometryStopwatch.Elapsed.TotalSeconds:F1}s.");
                geometryStopwatch.Stop();

                try { ssetReuse.Delete(); } catch { }

                // 2c. ERASE unresolvable structures
                // FIX: Removed the redundant first (setvar "QAFLAGS" 0) that was here before ERASE
                // It was triggering a premature regen while the command queue was still processing EXPLODE
                SendCommandSafe(doc, "\x03\x03");
                SendCommandSafe(doc, $"(if (setq ss (ssget \"_X\" {whitelistFilter})) (command \"_.ERASE\" ss \"\"))\n");

                // FIX: Single ERASE wait — only 500ms since REGENMODE is suppressed
                System.Threading.Thread.Sleep(500);

                // ─── Restore display settings ───
                LoggerService.LogTransaction("PLUGIN", "PrepareGeometry: restoring regen and cmdecho before QAFLAGS.");
                SendCommandSafe(doc, "(setvar \"REGENMODE\" 1)\n");
                SendCommandSafe(doc, "(setvar \"CMDECHO\" 1)\n");

                // FIX: ONE QAFLAGS call — at the very end, after BricsCAD has finished all queued work
                LoggerService.LogTransaction("PLUGIN", "PrepareGeometry: issuing final QAFLAGS reset.");
                SendCommandSafe(doc, "(setvar \"QAFLAGS\" 0)\n");

                return $"Geometry Prepared Natively: Executed {passCount} global wipe cycles to explode complex entities recursively, and finalized by erasing unresolvable objects.";
            }
            catch (Exception ex)
            {
                return $"Error preparing geometry: {ex.Message}";
            }
        }
    }
}
