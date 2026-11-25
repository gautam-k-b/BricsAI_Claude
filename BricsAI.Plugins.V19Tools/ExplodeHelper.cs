using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsAI.Plugins.V19Tools
{
    /// <summary>
    /// Shared iterative-explode engine used by both PrepareGeometry (full proofing path)
    /// and ExplodeWithBoothLock (standalone button).  The two callers were previously
    /// duplicate implementations; this class is the single source of truth.
    ///
    /// Key behaviours:
    ///   • Re-unlocks all non-booth, non-frozen layers at the start of EVERY pass, so layers
    ///     that appear inside exploded blocks are always reachable on the next pass.
    ///   • Re-enforces booth-layer locks after each unlock sweep.
    ///   • No hard pass cap — runs until the time limit or until truly stuck.
    ///   • Deletes ONLY what can never be useful: POINT and 3DFACE up front, and, once the loop
    ///     has stalled, HATCH and ATTDEF entities that could not be exploded. Everything else that
    ///     could not be exploded is LEFT IN THE DRAWING and reported by type; removing it is a
    ///     separate, explicit user request (delete_non_standard_entities).
    ///   • Never wipes layer 0 and never changes the current layer.
    /// </summary>
    internal static class ExplodeHelper
    {
        internal static readonly HashSet<string> BoothLayers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Expo_BoothOutline", "Expo_BoothNumber",
                "Expo_MaxBoothOutline", "Expo_MaxBoothNumber",
                // Temporary booth layers made by the opt-in pre-explode separation: never exploded or unlocked.
                "Proposed_BoothOutline", "Proposed_BoothNumber"
            };

        // Default (all drawings): the original whitelist - everything else is exploded.
        private static readonly short[]  WTypesDefault = { -4, -4, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
        private static readonly object[] WDataDefault  = { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "OR>", "NOT>" };

        // Block-grouped-booth drawings only (ProofingOptions.BlockGroupedBooths): closed heavy POLYLINEs (booth boxes)
        // must stay polylines - exploding them would break every booth into four loose lines. The COM filter
        // approximates this by whitelisting POLYLINE outright; the LISP filter below is exact (closed flag only).
        private static readonly short[]  WTypesBooth = { -4, -4, 0, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
        private static readonly object[] WDataBooth  = { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "POLYLINE", "OR>", "NOT>" };

        private static short[]  WTypes => BricsAI.Core.ProofingOptions.BlockGroupedBooths ? WTypesBooth : WTypesDefault;
        private static object[] WData  => BricsAI.Core.ProofingOptions.BlockGroupedBooths ? WDataBooth : WDataDefault;

        // LISP that selects all non-whitelisted entities and explodes them in one command call.
        private const string ExpLispDefault =
            "(if (setq ss (ssget \"_X\" '((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\")))) (command \"_.EXPLODE\" ss \"\"))\n";

        private const string ExpLispBooth =
            "(if (setq ss (ssget \"_X\" '((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"<AND\") (0 . \"POLYLINE\") (-4 . \"&\") (70 . 1) (-4 . \"AND>\") (-4 . \"OR>\") (-4 . \"NOT>\")))) (command \"_.EXPLODE\" ss \"\"))\n";

        private static string ExpLisp => BricsAI.Core.ProofingOptions.BlockGroupedBooths ? ExpLispBooth : ExpLispDefault;

        // Explodes every block reference on a non-booth layer one at a time and puts any resulting layer-0
        // entity back on the block reference's own layer (EXPLODE otherwise leaves booth outlines on layer 0).
        // Nested blocks inherit the restored layer, so the chain survives the following passes.
        private const string RestoreDefun =
            "(defun ba-xr ( / ss i en lay old nw d) " +
            "(if (setq ss (ssget \"_X\" '((0 . \"INSERT\") (-4 . \"<NOT\") (8 . \"Expo_*Booth*,Proposed_Booth*\") (-4 . \"NOT>\")))) " +
            "(progn (setq i 0) (while (< i (sslength ss)) " +
            "(setq en (ssname ss i) lay (cdr (assoc 8 (entget en))) old (entlast)) " +
            "(command \"_.EXPLODE\" en) " +
            "(setq nw (if old (entnext old) (entnext))) " +
            "(while nw (setq d (entget nw)) " +
            "(if (and (= (cdr (assoc 8 d)) \"0\") (/= lay \"0\")) (vl-catch-all-apply 'entmod (list (subst (cons 8 lay) (assoc 8 d) d)))) " +
            "(setq nw (entnext nw))) " +
            "(setq i (1+ i))))) (princ))\n";

        /// <summary>Locks the four booth output layers plus any caller-supplied extras.</summary>
        internal static void LockBoothLayers(object docObj, IEnumerable<string>? extra = null)
        {
            dynamic doc = docObj;
            foreach (var name in BoothLayers)
                try { doc.Layers.Item(name).Lock = true; } catch { }
            if (extra != null)
                foreach (var name in extra)
                    try { doc.Layers.Item(name).Lock = true; } catch { }
        }

        /// <summary>
        /// Unlocks every non-booth, non-frozen layer.  Called at the start of each pass so that
        /// layers created during block explosions (which arrive locked in some drawings) are
        /// immediately reachable in the next explode cycle.
        /// </summary>
        internal static void UnlockNonBoothLayers(object docObj)
        {
            dynamic doc = docObj;
            try
            {
                var layers = doc?.Layers;
                if (layers == null) return;
                int n = layers.Count;
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        var lyr = layers.Item(i);
                        if (BoothLayers.Contains((string)lyr.Name)) continue;
                        if ((bool)lyr.Freeze) continue;
                        lyr.Lock = false;
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Core iterative explode engine.
        /// </summary>
        /// <param name="docObj">Live BricsCAD COM document (passed as object to keep return type statically-typed;
        ///   pass with explicit cast: <c>object docObj = doc;</c> before calling).</param>
        /// <param name="extraLockedLayers">Additional layers to keep locked every pass
        ///   (e.g. vendor source layers that map to booth targets).</param>
        /// <param name="timeLimitSeconds">Wall-clock budget (default 300 s).</param>
        /// <param name="stuckLimit">Consecutive no-progress passes before giving up (default 5).</param>
        /// <returns>(passCount, totalReduced, remaining, deletedSummary, remainingByType) — remaining is the
        /// number of non-standard entities still in the drawing (never erased); deletedSummary lists the
        /// POINT/3DFACE/HATCH/ATTDEF entities that were deleted; remainingByType is e.g. "BlockReference x3".</returns>
        /// <summary>Result text of the last booth separation (empty when the option is off).</summary>
        internal static string LastProposalSummary { get; private set; } = "";

        internal static (int passCount, int totalReduced, int remaining, string deletedSummary, string remainingByType) Run(
            object docObj,
            IEnumerable<string>? extraLockedLayers = null,
            int timeLimitSeconds = 300,
            int stuckLimit = 5)
        {
            dynamic doc = docObj; // Cast here so the return type stays statically typed for callers
            doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");
            doc.SendCommand("(setvar \"CMDECHO\" 0)\n");
            doc.SendCommand("(setvar \"QATOL\" 0.001)\n");

            // ── Pre-clean: POINT and 3DFACE cannot be exploded and carry no useful geometry ──
            var deleted = new Dictionary<string, int>();
            deleted["POINT"] = CountByType(doc, "POINT");
            deleted["3DFACE"] = CountByType(doc, "3DFACE");
            doc.SendCommand("\x03\x03");
            doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"POINT\")))) (command \"_.ERASE\" ss \"\"))\n");
            System.Threading.Thread.Sleep(200);
            doc.SendCommand("\x03\x03");
            doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"3DFACE\")))) (command \"_.ERASE\" ss \"\"))\n");
            System.Threading.Thread.Sleep(200);

            // ── Flatten splines — EXPLODE cannot handle them natively ───────────────────
            string splName = "BA_EH_Spl_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            dynamic? splSset = doc.SelectionSets.Add(splName);
            try
            {
                splSset.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { "SPLINE" });
                if (splSset.Count > 0)
                {
                    doc.SendCommand("\x03\x03");
                    doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");
                    doc.SendCommand("(if (setq ss (ssget \"_X\" '((0 . \"SPLINE\")))) (sssetfirst nil ss))\n");
                    doc.SendCommand("FLATTEN\n\n\n");
                    doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");
                    System.Threading.Thread.Sleep(500);
                }
            }
            finally { try { splSset!.Delete(); } catch { } }

            // ── Iterative explode loop ──────────────────────────────────────────────────
            string ssetName = "BA_EH_Loop";
            dynamic? sset = null;
            try { sset = doc.SelectionSets.Item(ssetName); sset!.Delete(); } catch { }
            sset = doc.SelectionSets.Add(ssetName);

            int passCount    = 0;
            int totalReduced = 0;
            int previousCount = -1;
            int stuckPasses  = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.Elapsed.TotalSeconds < timeLimitSeconds)
            {
                passCount++;

                // Re-unlock every non-booth, non-frozen layer at the start of each pass.
                // Blocks exploded in the previous pass may have deposited entities onto layers
                // that were locked inside the block definition — those layers are now in the
                // main drawing's table and must be unlocked before the next EXPLODE can reach them.
                UnlockNonBoothLayers(doc);
                LockBoothLayers(doc, extraLockedLayers);

                sset.Clear();
                sset.Select(5, Type.Missing, Type.Missing, WTypes, WData);
                int countBefore = sset.Count;

                if (countBefore == 0) break;  // All done — perfect geometry

                if (countBefore == previousCount)
                {
                    if (++stuckPasses >= stuckLimit) break;  // Truly unexplodable remainder
                }
                else
                {
                    stuckPasses = 0;
                }
                previousCount = countBefore;

                doc.SendCommand("(setvar \"QAFLAGS\" 1)\n");
                int insertCount = BricsAI.Core.ProofingOptions.BlockGroupedBooths ? CountInserts(doc) : 0;
                if (insertCount > 0)
                {
                    doc.SendCommand(RestoreDefun);
                    doc.SendCommand("(ba-xr)\n");
                    System.Threading.Thread.Sleep(Math.Min(20000, Math.Max(300, insertCount * 15)));
                }
                doc.SendCommand(ExpLisp);
                doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");

                int sleep = Math.Min(800, Math.Max(200, countBefore / 10));
                System.Threading.Thread.Sleep(sleep);

                sset.Clear();
                sset.Select(5, Type.Missing, Type.Missing, WTypes, WData);
                totalReduced += countBefore - sset.Count;
            }

            try { sset!.Delete(); } catch { }

            // ── Delete only HATCH / ATTDEF that the loop could not explode ──────────────
            // Useless once they cannot be exploded any further. Deleted via COM (synchronous, so the
            // counts below are reliable). Entities on locked layers (e.g. booth layers) throw and
            // are left alone.
            foreach (var type in new[] { "HATCH", "ATTDEF" })
            {
                int n = DeleteByType(doc, type);
                if (n > 0) deleted[type] = n;
            }

            // ── Count remaining non-standard entities for reporting ─────────────────────
            int remaining = 0;
            string cntName = "BA_EH_Cnt";
            dynamic? cntSset = null;
            try
            {
                try { cntSset = doc.SelectionSets.Item(cntName); cntSset!.Delete(); } catch { }
                cntSset = doc.SelectionSets.Add(cntName);
                cntSset.Select(5, Type.Missing, Type.Missing, WTypes, WData);
                remaining = cntSset.Count;
            }
            finally { try { cntSset?.Delete(); } catch { } }

            string remainingByType = remaining > 0 ? DescribeRemaining(doc) : "";

            doc.SendCommand("(setvar \"CMDECHO\" 1)\n");

            // Opt-in, drawing-specific: separate booth outlines / numbers / building content into Proposed_ layers.
            LastProposalSummary = "";
            if (BricsAI.Core.ProofingOptions.BlockGroupedBooths)
                LastProposalSummary = BoothProposer.Run(docObj);

            string deletedSummary = string.Join(", ", deleted.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} x{kv.Value}"));
            return (passCount, totalReduced, remaining, deletedSummary, remainingByType);
        }

        /// <summary>Block references in the drawing (exploded with parent-layer restore).</summary>
        private static int CountInserts(dynamic doc)
        {
            string name = "BA_EH_I_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            dynamic? ss = null;
            try
            {
                ss = doc.SelectionSets.Add(name);
                ss.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { "INSERT" });
                return (int)ss.Count;
            }
            catch { return 0; }
            finally { try { ss?.Delete(); } catch { } }
        }

        private static int CountByType(dynamic doc, string dxfType)
        {
            string name = "BA_EH_C_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            dynamic? ss = null;
            try
            {
                ss = doc.SelectionSets.Add(name);
                ss.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { dxfType });
                return (int)ss.Count;
            }
            catch { return 0; }
            finally { try { ss?.Delete(); } catch { } }
        }

        /// <summary>Deletes every entity of the given DXF type via COM. Returns how many were deleted.</summary>
        private static int DeleteByType(dynamic doc, string dxfType)
        {
            string name = "BA_EH_D_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            dynamic? ss = null;
            int deleted = 0;
            try
            {
                ss = doc.SelectionSets.Add(name);
                ss.Select(5, Type.Missing, Type.Missing, new short[] { 0 }, new object[] { dxfType });
                for (int i = (int)ss.Count - 1; i >= 0; i--)
                {
                    try { ss.Item(i).Delete(); deleted++; } catch { }
                }
            }
            catch { }
            finally { try { ss?.Delete(); } catch { } }
            return deleted;
        }

        /// <summary>Groups the entities still left after the loop by ObjectName, e.g. "BlockReference x3, MText x1".</summary>
        private static string DescribeRemaining(dynamic doc)
        {
            string name = "BA_EH_R_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            dynamic? ss = null;
            try
            {
                ss = doc.SelectionSets.Add(name);
                ss.Select(5, Type.Missing, Type.Missing, WTypes, WData);
                var counts = new Dictionary<string, int>();
                int n = Math.Min((int)ss.Count, 5000);
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        string t = ((string)ss.Item(i).ObjectName).Replace("AcDb", "");
                        counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
                    }
                    catch { }
                }
                return string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
            }
            catch { return ""; }
            finally { try { ss?.Delete(); } catch { } }
        }
    }
}
