using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsAI.Core.Mock
{
    /// <summary>
    /// Interprets the small, fixed set of LISP command shapes this codebase's plugins actually
    /// generate (ssget-filtered CHPROP/ERASE/EXPLODE/FLATTEN, LAYDEL, plus setvar/PURGE no-ops)
    /// and applies their effect to the in-memory mock drawing. This is NOT a general LISP
    /// interpreter — it only understands the exact patterns produced by GeometryToolsPlugin,
    /// LayerToolsPlugin, and ExplodeTool. If a plugin starts emitting a new LISP shape, this needs
    /// a matching case added; anything unrecognized is a safe no-op.
    /// </summary>
    internal static class MockLispInterpreter
    {
        public static void Execute(MockDocument doc, string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            var filtered = ParseSsgetFilter(doc, command);
            if (filtered != null)
            {
                doc.PendingSelection = filtered;
            }

            if (ContainsWord(command, "CHPROP"))
            {
                ApplyChprop(doc, command);
                return;
            }

            if (ContainsWord(command, "LAYDEL"))
            {
                ApplyLaydel(doc, command);
                return;
            }

            if (ContainsWord(command, "ERASE"))
            {
                if (doc.PendingSelection != null)
                    foreach (var e in doc.PendingSelection) e.IsDeleted = true;
                doc.PendingSelection = null;
                return;
            }

            if (ContainsWord(command, "EXPLODE") || ContainsWord(command, "FLATTEN"))
            {
                if (doc.PendingSelection != null)
                    foreach (var e in doc.PendingSelection) SimplifyToPolyline(e);
                doc.PendingSelection = null;
                return;
            }

            // setvar, -PURGE, CLAYER, sssetfirst-only, and anything else: no-op.
            // If a filter was parsed above with no recognized verb in the same call, it stays
            // pending for whatever the next SendCommand call does (matches the real two-call
            // sssetfirst-then-bare-verb pattern used by ExplodeTool/PrepareGeometry).
        }

        private static bool ContainsWord(string command, string word)
            => command.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        private static void SimplifyToPolyline(MockEntity e)
        {
            e.DxfType = "LWPOLYLINE";
            e.ObjectName = "AcDbPolyline";
        }

        private static void ApplyChprop(MockDocument doc, string command)
        {
            int idx = command.IndexOf("CHPROP", StringComparison.OrdinalIgnoreCase);
            string tail = command.Substring(idx);
            var m = Regex.Match(tail, "LA\"\\s+\"(?<tgt>[^\"]*)\"");
            if (m.Success && doc.PendingSelection != null)
            {
                string target = m.Groups["tgt"].Value;
                doc.Layers.Add(target);
                foreach (var e in doc.PendingSelection) e.Layer = target;
            }
            doc.PendingSelection = null;
        }

        private static void ApplyLaydel(MockDocument doc, string command)
        {
            var m = Regex.Match(command, "LAYDEL\"\\s+\"(?<name>[^\"]*)\"");
            if (!m.Success) return;

            string name = m.Groups["name"].Value;
            foreach (var e in doc.Entities.Where(x => string.Equals(x.Layer, name, StringComparison.OrdinalIgnoreCase)))
                e.IsDeleted = true;
            doc.Layers.Remove(name);
        }

        private static List<MockEntity>? ParseSsgetFilter(MockDocument doc, string command)
        {
            string? body = ExtractBalancedSsgetBody(command);
            if (body == null) return null;

            bool isNot = body.IndexOf("<NOT", StringComparison.OrdinalIgnoreCase) >= 0;

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(body, "\\(0\\s*\\.\\s*\"(?<t>[^\"]*)\"\\)"))
                foreach (var part in m.Groups["t"].Value.Split(','))
                    types.Add(part.Trim());

            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(body, "\\(8\\s*\\.\\s*\"(?<l>[^\"]*)\"\\)"))
                layers.Add(m.Groups["l"].Value);

            IEnumerable<MockEntity> pool = doc.Entities.Where(e => !e.IsDeleted);

            if (types.Count > 0)
            {
                pool = isNot
                    ? pool.Where(e => !types.Contains(e.DxfType))
                    : pool.Where(e => types.Contains(e.DxfType));
            }

            if (layers.Count > 0)
            {
                pool = pool.Where(e => layers.Contains(e.Layer));
            }

            return pool.ToList();
        }

        /// <summary>
        /// Finds `ssget "_X" '(...)` and returns the fully balanced parenthesized body, using a
        /// manual bracket scan rather than regex since the filter can nest arbitrarily
        /// (e.g. the &lt;NOT &lt;OR ... OR&gt; NOT&gt; whitelist filter).
        /// </summary>
        private static string? ExtractBalancedSsgetBody(string command)
        {
            int ssgetIdx = command.IndexOf("ssget", StringComparison.OrdinalIgnoreCase);
            if (ssgetIdx < 0) return null;

            int openIdx = command.IndexOf("'(", ssgetIdx, StringComparison.Ordinal);
            if (openIdx < 0) return null;

            int start = openIdx + 1; // position of the '(' itself
            int depth = 0;
            int i = start;
            for (; i < command.Length; i++)
            {
                if (command[i] == '(') depth++;
                else if (command[i] == ')')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }

            if (depth != 0) return null; // unbalanced — malformed input, bail out safely
            return command.Substring(start, i - start);
        }
    }
}
