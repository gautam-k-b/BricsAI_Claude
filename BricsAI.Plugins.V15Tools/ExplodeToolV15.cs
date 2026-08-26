using BricsAI.Core;

namespace BricsAI.Plugins.V15Tools
{
    public class ExplodeToolV15 : IToolPlugin
    {
        public string Name => "Explode All Entities";
        public string Description => "Explodes all block references and complex entities in the BricsCAD V15 drawing.";
        public int TargetVersion => 15;

        public string GetPromptExample()
        {
            return "User: 'Explode all the blocks'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"EXPLODE\", \"lisp_code\": \"(command \\\"_.EXPLODE\\\" (ssget \\\"_X\\\") \\\"\\\")\" }] }\n\n" +
                   "User: 'check for the type MTEXT in quick select and if available, explode it'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"QSELECT_EXPLODE\", \"lisp_code\": \"NET:QSELECT_EXPLODE:MTEXT\" }] }\n\n" +
                   "User: 'also explode 3D Solids, Aligned Dimensions, and Multileaders'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"QSELECT_EXPLODE\", \"lisp_code\": \"NET:QSELECT_EXPLODE:3D SOLID\" }, { \"command_name\": \"QSELECT_EXPLODE\", \"lisp_code\": \"NET:QSELECT_EXPLODE:ALIGNED DIMENSION\" }, { \"command_name\": \"QSELECT_EXPLODE\", \"lisp_code\": \"NET:QSELECT_EXPLODE:MULTILEADER\" }] }\n\n" +
                   "User: 'delete items which are not in the standard list'\n" +
                   "Response: { \"tool_calls\": [{ \"command_name\": \"DELETE_NON_STANDARD\", \"lisp_code\": \"NET:DELETE_NON_STANDARD\" }] }";
        }

        public bool CanExecute(string netCommandName)
        {
            return netCommandName != null && (
                netCommandName.StartsWith("NET:QSELECT_EXPLODE") ||
                netCommandName.StartsWith("NET:DELETE_NON_STANDARD") ||
                netCommandName.StartsWith("NET:EXPLODE_WITH_BOOTH_LOCK"));
        }

        public string Execute(dynamic doc, string netCmd)
        {
            if (netCmd.StartsWith("NET:QSELECT_EXPLODE:"))
            {
                var parts = netCmd.Split(':');
                string? itemType = parts.Length > 1 ? parts[1].Trim() : null;
                return QSelectExplode(doc, itemType);
            }
            if (netCmd.StartsWith("NET:DELETE_NON_STANDARD"))
            {
                return DeleteNonStandard(doc);
            }
            if (netCmd.StartsWith("NET:EXPLODE_WITH_BOOTH_LOCK"))
            {
                return ExplodeWithBoothLock(doc);
            }
            return "Error: Command not explicitly handled in ExplodeToolV15.";
        }

        private string DeleteNonStandard(dynamic doc)
        {
            try
            {
                doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");
                string whitelistFilter = "'((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"POLYLINE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\"))";
                doc.SendCommand($"(if (setq ss (ssget \"_X\" {whitelistFilter})) (sssetfirst nil ss))\n");
                doc.SendCommand("_.ERASE\n");
                return "Deleted non-standard items (kept only ARC, LINE, CIRCLE, ELLIPSE, POLYLINE, TEXT, and SOLID).";
            }
            catch (System.Exception ex)
            {
                return $"Error deleting non-standard items: {ex.Message}";
            }
        }

        private string ExplodeWithBoothLock(dynamic doc)
        {
            try
            {
                var boothLayers = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    "Expo_BoothOutline", "Expo_BoothNumber", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
                };

                // Step 1: Unlock all non-booth, non-frozen layers so explode can reach everything
                var layers = doc?.Layers;
                if (layers != null)
                {
                    for (int i = 0; i < layers.Count; i++)
                    {
                        try
                        {
                            var lyr = layers.Item(i);
                            if (boothLayers.Contains((string)lyr.Name)) continue;
                            if ((bool)lyr.Freeze) continue;
                            lyr.Lock = false;
                        }
                        catch { }
                    }
                }

                // Step 2: Lock the four booth output layers
                try { doc.Layers.Item("Expo_BoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_BoothOutline").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothNumber").Lock = true; } catch { }
                try { doc.Layers.Item("Expo_MaxBoothOutline").Lock = true; } catch { }

                doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");
                doc.SendCommand("(setvar \"CMDECHO\" 0)\n");
                doc.SendCommand("(setvar \"QATOL\" 0.001)\n");

                // Step 3a: Erase POINT entities — they cannot be exploded and have no use in final geometry
                doc.SendCommand("\x03\x03");
                doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"POINT\")))) (command \"_.ERASE\" ss \"\"))\n");
                System.Threading.Thread.Sleep(200);

                // Step 3b: Erase 3DFACE entities — EXPLODE silently fails on them; only deletion works
                doc.SendCommand("\x03\x03");
                doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"3DFACE\")))) (command \"_.ERASE\" ss \"\"))\n");
                System.Threading.Thread.Sleep(200);

                // Step 3c: Flatten splines — EXPLODE cannot handle them; FLATTEN converts to polylines
                string splineSsetName = "BA_EBL_Spline_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                var splineSset = doc.SelectionSets.Add(splineSsetName);
                try
                {
                    splineSset.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { "SPLINE" });
                    if (splineSset.Count > 0)
                    {
                        doc.SendCommand("\x03\x03");
                        doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");
                        doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"SPLINE\")))) (sssetfirst nil ss))\n");
                        doc.SendCommand("FLATTEN\n\n\n");
                        doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");
                        System.Threading.Thread.Sleep(500);
                    }
                }
                finally
                {
                    try { splineSset.Delete(); } catch { }
                }

                // Step 4: Iterative explode loop — all non-whitelist types, up to 30 passes / 120 seconds
                short[] wType = new short[] { -4, -4, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
                object[] wData = new object[] { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "OR>", "NOT>" };
                string whitelistLisp = "(if (setq ss (ssget \"_X\" '((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\")))) (sssetfirst nil ss))\n";

                int maxPasses = 30;
                int passCount = 0;
                int totalReduced = 0;
                int previousCount = -1;
                int stuckPasses = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                string ssetName = "BA_EBL_Reuse";
                dynamic? sset = null;
                try { sset = doc.SelectionSets.Item(ssetName); sset.Delete(); } catch { }
                sset = doc.SelectionSets.Add(ssetName);

                while (passCount < maxPasses && sw.Elapsed.TotalSeconds < 120)
                {
                    passCount++;
                    sset.Clear();
                    sset.Select(5, Type.Missing, Type.Missing, wType, wData);
                    int countBefore = sset.Count;

                    if (countBefore == 0) break;

                    if (countBefore == previousCount)
                    {
                        stuckPasses++;
                        if (stuckPasses >= 3) break; // No progress for 3 consecutive passes
                    }
                    else
                    {
                        stuckPasses = 0;
                    }
                    previousCount = countBefore;

                    doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");
                    doc.SendCommand(whitelistLisp);
                    doc.SendCommand("_.EXPLODE\n");
                    doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");

                    int sleepMs = System.Math.Min(800, System.Math.Max(200, countBefore / 10));
                    System.Threading.Thread.Sleep(sleepMs);

                    sset.Clear();
                    sset.Select(5, Type.Missing, Type.Missing, wType, wData);
                    totalReduced += countBefore - sset.Count;
                }

                try { sset.Delete(); } catch { }

                // Count remaining non-standard entities (informational — no erase)
                int remaining = 0;
                string countName = "BA_EBL_Count";
                dynamic? countSset = null;
                try
                {
                    try { countSset = doc.SelectionSets.Item(countName); countSset.Delete(); } catch { }
                    countSset = doc.SelectionSets.Add(countName);
                    countSset.Select(5, Type.Missing, Type.Missing, wType, wData);
                    remaining = countSset.Count;
                }
                finally
                {
                    try { countSset?.Delete(); } catch { }
                }

                doc.SendCommand("(setvar \"CMDECHO\" 1)\n");

                string suffix = remaining > 0
                    ? $"{remaining} non-standard entities remain (unexplodable — locked, xref-attached, or dynamic blocks)."
                    : "All non-standard entities resolved.";
                return $"Explode with booth lock complete: {passCount} passes, {totalReduced} entities exploded. {suffix}";
            }
            catch (System.Exception ex)
            {
                return $"Error in ExplodeWithBoothLock: {ex.Message}";
            }
        }

        private string QSelectExplode(dynamic doc, string? itemType = null)
        {
            if (string.IsNullOrEmpty(itemType)) return "Error: No item type specified.";
            try
            {
                string filterType = itemType.ToUpper();
                if (filterType == "ROTATED DIMENSION" || filterType == "ALIGNED DIMENSION") filterType = "DIMENSION";
                if (filterType == "BLOCK REFERENCE") filterType = "INSERT";
                if (filterType == "3D SOLID") filterType = "3DSOLID";
                if (filterType == "MULTILEADER") filterType = "MULTILEADER";
                if (filterType == "ATTRIBUTE DEFINITION") filterType = "ATTDEF";
                if (filterType == "POLYFACE MESH") filterType = "POLYFACEMESH";
                if (filterType == "SURFACE") filterType = "SURFACE";
                if (filterType == "REGION") filterType = "REGION";
                if (filterType == "MTEXT") filterType = "MTEXT";

                doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");

                if (filterType == "SPLINE")
                {
                    try
                    {
                        doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"SPLINE\")))) (sssetfirst nil ss))\n");
                        doc.SendCommand("FLATTEN\n\n\n");
                        return $"Quick Select: Found and natively FLATTENED Spline entities.";
                    }
                    catch (System.Exception ex)
                    {
                        return $"Error flattening SPLINE: {ex.Message}";
                    }
                }

                int totalExploded = 0;
                int passCount = 0;
                int maxPasses = 10; // Failsafe to prevent infinite COM loop

                string ssetName = "BA_QSel_" + System.Guid.NewGuid().ToString("N").Substring(0, 10);

                while (passCount < maxPasses)
                {
                    passCount++;
                    var sset = doc.SelectionSets.Add(ssetName);
                    try
                    {
                        sset.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { filterType });
                        int countBefore = sset.Count;

                        if (countBefore == 0) break; // No more entities of this type

                        doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");
                        doc.SendCommand($"(if (setq ss (ssget \"_X\" '((0 . \"{filterType}\")))) (sssetfirst nil ss))\n");
                        doc.SendCommand("_.EXPLODE\n");
                        doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");
                        System.Threading.Thread.Sleep(500);

                        // Count-after check: if the count didn't change the type is unexplodable — skip it.
                        sset.Clear();
                        sset.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { filterType });
                        int countAfter = sset.Count;

                        int reduced = countBefore - countAfter;
                        if (reduced <= 0)
                        {
                            return $"Quick Select: Found {countBefore} '{itemType}' entities but none could be exploded (count unchanged after attempt). Skipping — they may be locked, xref-attached, or dynamic blocks.";
                        }

                        totalExploded += reduced;
                    }
                    finally
                    {
                        try { sset.Delete(); } catch { }
                    }
                }

                if (totalExploded > 0)
                {
                    return $"Quick Select: Successfully reduced '{itemType}' by {totalExploded} entities across {passCount} passes.";
                }
                else
                {
                    return $"Quick Select check: No '{itemType}' entities found in the drawing.";
                }
            }
            catch (System.Exception ex)
            {
                return $"Error executing QSelect Explode: {ex.Message}";
            }
        }
    }
}
