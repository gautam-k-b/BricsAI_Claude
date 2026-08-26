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
                   "Response: { \"tool_calls\": [{ \"command_name\": \"PREPARE_GEOMETRY\", \"lisp_code\": \"NET:PREPARE_GEOMETRY\" }] }\n\n" +
                   "User: 'Select the building outline' (the largest-bounding-box closed polyline on a layer)\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"SELECT_BUILDING_LINES\", \"lisp_code\": \"NET:SELECT_BUILDING_LINES:Expo_Building\" }] }";
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
                // Suppress display overhead for the entire run — ExplodeHelper restores CMDECHO at the end.
                SendCommandSafe(doc, "(setvar \"REGENMODE\" 0)\n");

                // Lock booth output layers (canonical names, mapped vendor sources, heuristic fallback).
                ExplodeHelper.LockBoothLayers(doc);

                // Collect vendor source layers that map to any booth target — keep them locked every pass.
                var extraLocked = new List<string>();
                try
                {
                    var mappings = KnowledgeService.GetLayerMappingsDictionary();
                    if (mappings != null)
                    {
                        var boothTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "Expo_BoothOutline", "Expo_BoothNumber",
                            "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
                        };
                        foreach (var kvp in mappings)
                            if (boothTargets.Contains(kvp.Value.Trim()))
                                extraLocked.Add(kvp.Key.Trim());
                    }
                }
                catch { }

                // Heuristic fallback: also lock layers whose normalised name looks like a booth layer.
                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    try
                    {
                        var lyr = doc.Layers.Item(i);
                        string norm = ((string)lyr.Name).ToLower()
                            .Replace(" ", "").Replace("_", "").Replace("-", "");
                        if (norm.Contains("boothoutline") || norm.Contains("boothnumber") ||
                            norm.Contains("maxboothoutline") || norm.Contains("maxboothnumber"))
                            lyr.Lock = true;
                    }
                    catch { }
                }

                LoggerService.LogTransaction("PLUGIN", "PrepareGeometry: starting shared ExplodeHelper.Run.");
                object docObj = doc; // Cast to object so the tuple return type stays statically typed
                var (passCount, _, remaining) = ExplodeHelper.Run(docObj, extraLocked);

                // PrepareGeometry erases any truly unexplodable leftovers (hard cleanup for full proofing).
                if (remaining > 0)
                {
                    const string wl = "(if (setq ss (ssget \"_X\" '((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\")))) (command \"_.ERASE\" ss \"\"))\n";
                    SendCommandSafe(doc, "\x03\x03");
                    SendCommandSafe(doc, wl);
                    System.Threading.Thread.Sleep(500);
                }

                SendCommandSafe(doc, "(setvar \"REGENMODE\" 1)\n");
                SendCommandSafe(doc, "(setvar \"QAFLAGS\" 0)\n");

                LoggerService.LogTransaction("PLUGIN", $"PrepareGeometry: done — {passCount} passes, {remaining} remaining before hard erase.");

                string unexplodableNote = remaining > 0
                    ? $" WARNING: {remaining} entities could not be exploded after {passCount} passes and were erased (likely locked, xref-attached, or dynamic blocks). Review the drawing for missing geometry."
                    : " All complex entities were successfully exploded.";
                return $"Geometry Prepared Natively: Executed {passCount} global wipe cycles.{unexplodableNote}";
            }
            catch (Exception ex)
            {
                return $"Error preparing geometry: {ex.Message}";
            }
        }
    }
}
