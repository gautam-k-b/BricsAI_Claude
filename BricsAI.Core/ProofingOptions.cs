using System;

namespace BricsAI.Core
{
    /// <summary>
    /// Process-wide switches for drawing-specific proofing behaviour. Everything here is OFF by default so
    /// ordinary drawings are proofed exactly as before; a switch is turned on only for the kind of drawing
    /// that needs it (set from the Overlay checkbox, the MCP tool, or the environment variable below).
    /// </summary>
    public static class ProofingOptions
    {
        /// <summary>
        /// For drawings where booths are drawn as heavy closed polylines inside block groups (e.g. the
        /// SEMICON Japan layout): exploded block contents that land on layer 0 are moved back to the block
        /// reference's own layer, and closed heavy POLYLINEs (the booth boxes) are not exploded into lines.
        /// Default: off (env BRICSAI_BLOCK_GROUPED_BOOTHS=1 turns it on at start-up).
        /// </summary>
        public static bool BlockGroupedBooths { get; set; } =
            string.Equals(Environment.GetEnvironmentVariable("BRICSAI_BLOCK_GROUPED_BOOTHS"), "1", StringComparison.Ordinal);
    }
}
