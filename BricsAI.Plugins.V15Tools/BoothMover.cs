using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsAI.Plugins.V15Tools
{
    /// <summary>
    /// Explicit, user-requested move of entities from a vendor layer onto one of the protected booth
    /// output layers. Booth layers are never targets of the learned-mapping flow; this is the only
    /// sanctioned way to populate them, and only when the caller names the source layer and target.
    ///
    /// Command: NET:MOVE_TO_BOOTH:&lt;sourceLayer&gt;:&lt;boothLayer&gt;[:shapes|text|all]
    ///   shapes (default) - only closed rectangular polylines (the booth boxes) whose area is close to the
    ///                      layer's typical booth area; walls, fixtures and everything else stay put.
    ///   text             - only TEXT / MTEXT (booth numbers).
    ///   all              - every entity on the source layer.
    /// </summary>
    internal static class BoothMover
    {
        internal const string Prefix = "NET:MOVE_TO_BOOTH:";

        private static readonly HashSet<string> BoothTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Expo_BoothOutline", "Expo_BoothNumber", "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
        };

        internal static string Run(dynamic doc, string netCmd)
        {
            try
            {
                var parts = netCmd.Substring(Prefix.Length).Split(':');
                if (parts.Length < 2) return "Error: usage NET:MOVE_TO_BOOTH:<sourceLayer>:<boothLayer>[:shapes|text|all]";
                string src = parts[0].Trim();
                string tgt = parts[1].Trim();
                string mode = parts.Length > 2 ? parts[2].Trim().ToLowerInvariant() : "shapes";

                if (!BoothTargets.Contains(tgt))
                    return $"Error: '{tgt}' is not a booth output layer (Expo_BoothOutline, Expo_BoothNumber, Expo_MaxBoothOutline, Expo_MaxBoothNumber).";
                if (mode != "shapes" && mode != "text" && mode != "all")
                    return $"Error: unknown mode '{mode}' (use shapes, text or all).";
                if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase)) return "Error: source and target are the same layer.";

                try { doc.Layers.Item(src).Lock = false; }
                catch { return $"Error: source layer '{src}' not found."; }
                try { doc.Layers.Add(tgt); } catch { }

                dynamic tgtLayer = doc.Layers.Item(tgt);
                try { tgtLayer.Lock = false; } catch { }

                int moved = 0, candidates = 0, skipped = 0;
                string detail = "";
                dynamic? sset = null;
                try
                {
                    string name = "BA_MoveBooth_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    sset = doc.SelectionSets.Add(name);

                    if (mode == "all")
                        sset.Select(5, Type.Missing, Type.Missing, new short[] { 8 }, new object[] { src });
                    else if (mode == "text")
                        sset.Select(5, Type.Missing, Type.Missing, new short[] { 0, 8 }, new object[] { "TEXT,MTEXT", src });
                    else
                        sset.Select(5, Type.Missing, Type.Missing, new short[] { 0, 8 }, new object[] { "LWPOLYLINE,POLYLINE", src });

                    int count = (int)sset.Count;
                    var picked = new List<(dynamic ent, double area)>();
                    for (int i = 0; i < count; i++)
                    {
                        dynamic ent = sset.Item(i);
                        if (mode == "shapes")
                        {
                            if (TryBoothRect(ent, out double area)) picked.Add((ent, area));
                            else skipped++;
                        }
                        else picked.Add((ent, 0));
                    }
                    candidates = picked.Count;

                    if (mode == "shapes" && picked.Count > 0)
                    {
                        // Booth boxes share one typical size; ignore rectangles far from the median
                        // (title blocks, room outlines, fixtures drawn as rectangles).
                        var sorted = picked.Select(p => p.area).OrderBy(a => a).ToList();
                        double median = sorted[sorted.Count / 2];
                        var keep = picked.Where(p => p.area >= median * 0.25 && p.area <= median * 4.0).ToList();
                        skipped += picked.Count - keep.Count;
                        picked = keep;
                        detail = $" Typical booth area {median:0.##}.";
                    }

                    foreach (var p in picked)
                    {
                        try { p.ent.Layer = tgt; moved++; } catch { skipped++; }
                    }
                }
                finally
                {
                    try { sset?.Delete(); } catch { }
                    // Booth layers must always end locked.
                    try { tgtLayer.Lock = true; } catch { }
                }

                BricsAI.Core.LoggerService.LogTransaction("PLUGIN", $"MoveToBooth: '{src}' -> '{tgt}' ({mode}): moved {moved}, skipped {skipped}.");
                return $"Moved {moved} {(mode == "shapes" ? "booth box(es)" : mode == "text" ? "text object(s)" : "entities")} from '{src}' to '{tgt}' (mode {mode}); {skipped} other candidate(s) left on '{src}'.{detail} '{tgt}' is locked.";
            }
            catch (Exception ex)
            {
                return $"Error moving to booth layer: {ex.Message}";
            }
        }

        /// <summary>
        /// Evidence for the mapper: among (at most <paramref name="cap"/>) polylines on the layer, how many are closed
        /// rectangles and how many of those share one dominant size (booth boxes are all the same size).
        /// </summary>
        internal static (int rectangles, double dominantArea, int dominantCount, int sampled) SampleRectangles(dynamic selectionSets, string layer, int cap)
        {
            dynamic? ss = null;
            try
            {
                ss = selectionSets.Add("BricsAI_PollR_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                ss.Select(5, Type.Missing, Type.Missing, new short[] { 0, 8 }, new object[] { "LWPOLYLINE,POLYLINE", layer });
                int n = Math.Min((int)ss.Count, cap);
                var buckets = new Dictionary<long, (int count, double area)>();
                int rects = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!TryBoothRect(ss.Item(i), out double area)) continue;
                    rects++;
                    long key = (long)Math.Round(Math.Log10(area) * 50.0); // ~5% wide size buckets
                    buckets.TryGetValue(key, out var b);
                    buckets[key] = (b.count + 1, area);
                }
                if (buckets.Count == 0) return (0, 0, 0, n);
                var top = buckets.Values.OrderByDescending(b => b.count).First();
                return (rects, top.area, top.count, n);
            }
            catch { return (0, 0, 0, 0); }
            finally { try { ss?.Delete(); } catch { } }
        }

        /// <summary>True for a closed 4-vertex polyline whose corners are all right angles.</summary>
        internal static bool TryBoothRect(dynamic ent, out double area)
        {
            area = 0;
            try
            {
                if (!(bool)ent.Closed) return false;
                var raw = ((Array)ent.Coordinates).Cast<object>().Select(Convert.ToDouble).ToArray();
                int stride = string.Equals((string)ent.ObjectName, "AcDbPolyline", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
                int n = raw.Length / stride;
                if (n == 5 && Math.Abs(raw[0] - raw[4 * stride]) < 1e-6 && Math.Abs(raw[1] - raw[4 * stride + 1]) < 1e-6) n = 4;
                if (n != 4) return false;

                var pts = new (double x, double y)[4];
                for (int i = 0; i < 4; i++) pts[i] = (raw[i * stride], raw[i * stride + 1]);

                double[] len = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % 4]; var c = pts[(i + 2) % 4];
                    double e1x = b.x - a.x, e1y = b.y - a.y, e2x = c.x - b.x, e2y = c.y - b.y;
                    len[i] = Math.Sqrt(e1x * e1x + e1y * e1y);
                    double l2 = Math.Sqrt(e2x * e2x + e2y * e2y);
                    if (len[i] < 1e-9 || l2 < 1e-9) return false;
                    if (Math.Abs(e1x * e2x + e1y * e2y) > 1e-3 * len[i] * l2) return false;
                }
                area = len[0] * len[1];
                return area > 0;
            }
            catch { return false; }
        }
    }
}
