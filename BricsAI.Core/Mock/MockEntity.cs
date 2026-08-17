namespace BricsAI.Core.Mock
{
    /// <summary>
    /// A single mock drawing entity. All members are public because the real plugin code
    /// (in the separate BricsAI.Plugins.V15/V19Tools assemblies) accesses this purely via
    /// `dynamic` — cross-assembly dynamic dispatch only binds against public members.
    /// </summary>
    public class MockEntity
    {
        /// <summary>DXF group-0 type name (e.g. "LWPOLYLINE", "TEXT", "CIRCLE") — what ssget-style filters match against.</summary>
        public string DxfType { get; set; } = "LINE";

        /// <summary>COM ObjectName-style class name (e.g. "AcDbPolyline") — what PollLayerSemantics reports.</summary>
        public string ObjectName { get; set; } = "AcDbLine";

        public string Layer { get; set; } = "0";
        public bool Closed { get; set; }
        public double Area { get; set; }
        public double[] Coordinates { get; set; } = System.Array.Empty<double>();
        public double[] BoundMin { get; set; } = new double[] { 0, 0, 0 };
        public double[] BoundMax { get; set; } = new double[] { 0, 0, 0 };
        public string TextString { get; set; } = "";
        public double[] InsertionPoint { get; set; } = new double[] { 0, 0, 0 };

        /// <summary>Block name, used for INSERT/AcDbBlockReference entities.</summary>
        public string Name { get; set; } = "";

        public bool IsDeleted { get; set; }

        public void GetBoundingBox(out object minPt, out object maxPt)
        {
            minPt = BoundMin;
            maxPt = BoundMax;
        }

        public void Highlight(bool on)
        {
            // No visible effect in a headless mock — real BricsCAD highlights on screen.
        }

        public void Delete() => IsDeleted = true;
    }
}
