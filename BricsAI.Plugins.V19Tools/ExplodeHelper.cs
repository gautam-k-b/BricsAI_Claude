using System;
using System.Collections.Generic;

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
    ///   • Sets CLAYER = "0" before the loop so FLATTEN output lands on layer 0, then
    ///     erases all entities on layer 0 at the end (the layer itself is kept).
    /// </summary>
    internal static class ExplodeHelper
    {
        internal static readonly HashSet<string> BoothLayers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Expo_BoothOutline", "Expo_BoothNumber",
                "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            };

        private static readonly short[]  WTypes = { -4, -4, 0, 0, 0, 0, 0, 0, 0, -4, -4 };
        private static readonly object[] WData  = { "<NOT", "<OR", "ARC", "LINE", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "TEXT", "SOLID", "OR>", "NOT>" };

        // LISP that selects all non-whitelisted entities and explodes them in one command call.
        private const string ExpLisp =
            "(if (setq ss (ssget \"_X\" '((-4 . \"<NOT\") (-4 . \"<OR\") (0 . \"ARC\") (0 . \"LINE\") (0 . \"CIRCLE\") (0 . \"ELLIPSE\") (0 . \"LWPOLYLINE\") (0 . \"TEXT\") (0 . \"SOLID\") (-4 . \"OR>\") (-4 . \"NOT>\")))) (command \"_.EXPLODE\" ss \"\"))\n";

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
        /// <returns>(passCount, totalReduced, remaining) tuple.</returns>
        internal static (int passCount, int totalReduced, int remaining) Run(
            object docObj,
            IEnumerable<string>? extraLockedLayers = null,
            int timeLimitSeconds = 300,
            int stuckLimit = 5)
        {
            dynamic doc = docObj; // Cast here so the return type stays statically typed for callers
            doc.SendCommand("(setvar \"PICKFIRST\" 1)\n");
            doc.SendCommand("(setvar \"CMDECHO\" 0)\n");
            doc.SendCommand("(setvar \"QATOL\" 0.001)\n");
            // Active layer = "0" so FLATTEN output and any intermediate geometry land on layer 0,
            // which is erased at the end so junk does not accumulate in the final drawing.
            doc.SendCommand("(setvar \"CLAYER\" \"0\")\n");

            // ── Pre-clean: POINT and 3DFACE cannot be exploded ──────────────────────────
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
                doc.SendCommand(ExpLisp);
                doc.SendCommand("(setvar \"QAFLAGS\" 0)\n");

                int sleep = Math.Min(800, Math.Max(200, countBefore / 10));
                System.Threading.Thread.Sleep(sleep);

                sset.Clear();
                sset.Select(5, Type.Missing, Type.Missing, WTypes, WData);
                totalReduced += countBefore - sset.Count;
            }

            try { sset!.Delete(); } catch { }

            // ── Erase entities that accumulated on layer "0" during the run ─────────────
            // SendCommand is fire-and-forget; use a COM-verified retry loop so we don't
            // return before BricsCAD has finished the ERASE (including whitelisted types
            // like LWPOLYLINEs created by FLATTEN that land on layer "0").
            doc.SendCommand("\x03\x03");
            doc.SendCommand("(if (setq ss (ssget \"_X\" '((8 . \"0\")))) (command \"_.ERASE\" ss \"\"))\n");
            System.Threading.Thread.Sleep(600); // initial wait

            for (int l0Retry = 0; l0Retry < 3; l0Retry++)
            {
                string l0Name = "BA_EH_L0";
                dynamic? l0Sset = null;
                int l0Count = 0;
                try
                {
                    try { l0Sset = doc.SelectionSets.Item(l0Name); l0Sset!.Delete(); } catch { }
                    l0Sset = doc.SelectionSets.Add(l0Name);
                    l0Sset.Select(5, Type.Missing, Type.Missing, new short[] { 8 }, new object[] { "0" });
                    l0Count = l0Sset.Count;
                }
                finally { try { l0Sset?.Delete(); } catch { } }

                if (l0Count == 0) break; // confirmed empty — move on

                // Entities still present; erase again and wait progressively longer.
                doc.SendCommand("\x03\x03");
                doc.SendCommand("(if (setq ss (ssget \"_X\" '((8 . \"0\")))) (command \"_.ERASE\" ss \"\"))\n");
                System.Threading.Thread.Sleep(800 * (l0Retry + 1));
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

            doc.SendCommand("(setvar \"CMDECHO\" 1)\n");

            return (passCount, totalReduced, remaining);
        }
    }
}
