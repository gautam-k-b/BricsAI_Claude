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
            return netCommandName != null && (netCommandName.StartsWith("NET:QSELECT_EXPLODE") || netCommandName.StartsWith("NET:DELETE_NON_STANDARD"));
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
