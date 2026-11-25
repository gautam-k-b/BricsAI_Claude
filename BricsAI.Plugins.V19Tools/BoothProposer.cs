using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsAI.Plugins.V19Tools
{
    /// <summary>
    /// Opt-in (ProofingOptions.BlockGroupedBooths) separation step that runs at the end of the pre-explode:
    /// on layers that mix booth geometry with everything else it moves
    ///   - the booth boxes (many closed rectangles of one size)  -> Proposed_BoothOutline
    ///   - booth-number text (E4001, W1001, 125 ...)             -> Proposed_BoothNumber
    ///   - small closed rectangles (column-like)                  -> Proposed_Column
    ///   - the remaining non-text drawing content                 -> Proposed_Building
    /// Plain text that is not a booth number stays where it is. Nothing is deleted. The Proposed_ layers are
    /// temporary: the reviewer confirms the real A2Z layer for each in the mapping grid, and the Proposed_Booth*
    /// layers are locked so no later explode pass touches them.
    /// </summary>
    internal static class BoothProposer
    {
        internal const string OutlineLayer = "Proposed_BoothOutline";
        internal const string NumberLayer = "Proposed_BoothNumber";
        internal const string BuildingLayer = "Proposed_Building";
        internal const string ColumnLayer = "Proposed_Column";

        private const int MinSameSizeRectangles = 20;   // a layer needs at least this many equal booth boxes
        private const int MinBoothNumbers = 20;         // ... or this many booth-number labels
        private const double BoothNumberShare = 0.6;    // share of a layer's text that must look like booth numbers
        private const int Cap = 20000;

        private static readonly Regex NumberPattern = new Regex(@"^[A-Za-z]{0,2}\d{2,5}[A-Za-z]?$", RegexOptions.Compiled);
        private static readonly Regex MTextCodes = new Regex(@"\\[A-Za-z][^;\\]*;|[{}]|\\P", RegexOptions.Compiled);

        internal static bool IsProposedLayer(string name) =>
            name.StartsWith("Proposed_", StringComparison.OrdinalIgnoreCase);

        internal static string Run(object docObj)
        {
            dynamic doc = docObj;
            var report = new List<string>();
            try
            {
                var names = new List<string>();
                var layers = doc.Layers;
                for (int i = 0; i < (int)layers.Count; i++)
                {
                    try
                    {
                        var l = layers.Item(i);
                        string n = (string)l.Name;
                        if (n == "0" || n.Equals("Defpoints", StringComparison.OrdinalIgnoreCase)) continue;
                        if (n.StartsWith("Expo_", StringComparison.OrdinalIgnoreCase) || IsProposedLayer(n) ||
                            n.StartsWith("Deleted_", StringComparison.OrdinalIgnoreCase)) continue;
                        if ((bool)l.Freeze) continue;
                        names.Add(n);
                    }
                    catch { }
                }

                object selSets = doc.SelectionSets;
                var outlineSources = new List<(string layer, double area)>();
                var numberSources = new List<string>();

                foreach (var n in names)
                {
                    var (_, domArea, domCount, _) = BoothMover.SampleRectangles(selSets, n, Cap);
                    if (domCount >= MinSameSizeRectangles) { outlineSources.Add((n, domArea)); continue; }

                    var (total, numbers) = CountBoothNumbers(selSets, n);
                    if (numbers >= MinBoothNumbers && total > 0 && numbers >= BoothNumberShare * total) numberSources.Add(n);
                }

                if (outlineSources.Count == 0 && numberSources.Count == 0)
                    return "Booth separation: no layer with repeated same-size booth boxes or booth-number text was found - nothing moved.";

                foreach (var name in new[] { OutlineLayer, NumberLayer, BuildingLayer, ColumnLayer })
                    try { doc.Layers.Add(name); } catch { }
                foreach (var name in new[] { OutlineLayer, NumberLayer, BuildingLayer, ColumnLayer })
                    try { doc.Layers.Item(name).Lock = false; } catch { }

                foreach (var (layer, domArea) in outlineSources)
                {
                    try { doc.Layers.Item(layer).Lock = false; } catch { }
                    int booths = 0, columns = 0;
                    var polys = Select(selSets, new short[] { 0, 8 }, new object[] { "LWPOLYLINE,POLYLINE", layer }, out dynamic? polySet);
                    try
                    {
                        for (int i = 0; i < Math.Min(polys, Cap); i++)
                        {
                            dynamic e = polySet!.Item(i);
                            if (!BoothMover.TryBoothRect(e, out double area)) continue;
                            try
                            {
                                if (area >= domArea * 0.25 && area <= domArea * 4.0) { e.Layer = OutlineLayer; booths++; }
                                else if (area >= domArea * 0.01 && area < domArea * 0.25) { e.Layer = ColumnLayer; columns++; }
                            }
                            catch { }
                        }
                    }
                    finally { try { polySet?.Delete(); } catch { } }
                    MoveRemainderToBuilding(doc, layer);
                    report.Add($"'{layer}': {booths} booth box(es) -> {OutlineLayer}, {columns} small rectangle(s) -> {ColumnLayer}, other drawing content -> {BuildingLayer}");
                }

                foreach (var layer in numberSources)
                {
                    try { doc.Layers.Item(layer).Lock = false; } catch { }
                    int moved = 0;
                    var texts = Select(selSets, new short[] { 0, 8 }, new object[] { "TEXT,MTEXT", layer }, out dynamic? textSet);
                    try
                    {
                        for (int i = 0; i < Math.Min(texts, Cap); i++)
                        {
                            try
                            {
                                dynamic e = textSet!.Item(i);
                                if (NumberPattern.IsMatch(Clean((string)e.TextString))) { e.Layer = NumberLayer; moved++; }
                            }
                            catch { }
                        }
                    }
                    finally { try { textSet?.Delete(); } catch { } }
                    MoveRemainderToBuilding(doc, layer);
                    report.Add($"'{layer}': {moved} booth number(s) -> {NumberLayer}, other drawing content -> {BuildingLayer}");
                }

                // The booth layers must never be exploded or touched by the following passes.
                foreach (var name in new[] { OutlineLayer, NumberLayer })
                    try { doc.Layers.Item(name).Lock = true; } catch { }

                string summary = "Booth separation (pre-explode): " + string.Join("; ", report) + ". Proposed_ layers are temporary - confirm the final layers in the mapping grid.";
                BricsAI.Core.LoggerService.LogTransaction("PLUGIN", "BoothProposer: " + summary);
                return summary;
            }
            catch (Exception ex)
            {
                return "Booth separation failed: " + ex.Message;
            }
        }

        /// <summary>Everything still on the layer except text goes to Proposed_Building (plain text stays put).</summary>
        private static void MoveRemainderToBuilding(dynamic doc, string layer)
        {
            string esc = layer.Replace("\"", "\\\"");
            doc.SendCommand("\x03\x03");
            doc.SendCommand($"(if (setq ss (ssget \"_X\" '((8 . \"{esc}\") (-4 . \"<NOT\") (0 . \"TEXT,MTEXT\") (-4 . \"NOT>\")))) (command \"_.CHPROP\" ss \"\" \"_LA\" \"{BuildingLayer}\" \"\"))\n");
            System.Threading.Thread.Sleep(800);
        }

        private static (int total, int numbers) CountBoothNumbers(object selectionSets, string layer)
        {
            int total = 0, numbers = 0;
            int count = Select(selectionSets, new short[] { 0, 8 }, new object[] { "TEXT,MTEXT", layer }, out dynamic? set);
            try
            {
                for (int i = 0; i < Math.Min(count, 3000); i++)
                {
                    try
                    {
                        total++;
                        if (NumberPattern.IsMatch(Clean((string)set!.Item(i).TextString))) numbers++;
                    }
                    catch { }
                }
            }
            finally { try { set?.Delete(); } catch { } }
            return (total, numbers);
        }

        private static string Clean(string text) => MTextCodes.Replace(text ?? "", "").Trim();

        private static int Select(object selectionSets, short[] types, object[] data, out dynamic? set)
        {
            dynamic sets = selectionSets;
            set = null;
            try
            {
                set = sets.Add("BA_BP_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                set.Select(5, Type.Missing, Type.Missing, types, data);
                return (int)set.Count;
            }
            catch { return 0; }
        }
    }
}
