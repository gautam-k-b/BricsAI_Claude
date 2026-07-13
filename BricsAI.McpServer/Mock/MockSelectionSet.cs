using System.Collections.Generic;
using System.Linq;

namespace BricsAI.McpServer.Mock
{
    /// <summary>
    /// Mimics a COM AcadSelectionSet: built via Select(mode, pt1, pt2, filterType[], filterData[])
    /// using simple paired DXF-code filters — only codes 0 (entity type, comma-separated OR list)
    /// and 8 (layer, exact match) are used anywhere in this codebase's plugin code.
    /// </summary>
    public class MockSelectionSet
    {
        private readonly MockDocument _doc;
        private List<MockEntity> _matches = new();

        public string SetName { get; }

        internal MockSelectionSet(MockDocument doc, string name)
        {
            _doc = doc;
            SetName = name;
        }

        public int Count => _matches.Count;

        public MockEntity Item(int index) => _matches[index];

        public void Select(int mode, object pt1, object pt2, short[] filterType, object[] filterData)
        {
            IEnumerable<MockEntity> pool = _doc.Entities.Where(e => !e.IsDeleted);

            for (int i = 0; i < filterType.Length; i++)
            {
                short code = filterType[i];
                string val = filterData[i]?.ToString() ?? "";

                if (code == 0)
                {
                    var types = val.Split(',').Select(t => t.Trim().ToUpperInvariant()).ToHashSet();
                    pool = pool.Where(e => types.Contains(e.DxfType.ToUpperInvariant()));
                }
                else if (code == 8)
                {
                    pool = pool.Where(e => string.Equals(e.Layer, val, System.StringComparison.OrdinalIgnoreCase));
                }
            }

            _matches = pool.ToList();
        }

        public void Highlight(bool on) { }

        public void Clear() => _matches.Clear();

        public void Delete() => _doc.SelectionSets.Remove(SetName);
    }
}
