using System;
using System.Collections.Generic;

namespace BricsAI.McpServer.Mock
{
    /// <summary>
    /// Mimics the AcadSelectionSets collection: Add(name)/Item(name)/Delete, name lookups throw
    /// when missing (matching COM behavior) since plugin code relies on try/catch around Item().
    /// </summary>
    public class MockSelectionSets
    {
        private readonly MockDocument _doc;
        private readonly Dictionary<string, MockSelectionSet> _sets = new(StringComparer.OrdinalIgnoreCase);

        internal MockSelectionSets(MockDocument doc)
        {
            _doc = doc;
        }

        public MockSelectionSet Add(string name)
        {
            var set = new MockSelectionSet(_doc, name);
            _sets[name] = set;
            return set;
        }

        public MockSelectionSet Item(string name)
        {
            if (!_sets.TryGetValue(name, out var set))
                throw new InvalidOperationException($"Mock selection set '{name}' not found.");
            return set;
        }

        internal void Remove(string name) => _sets.Remove(name);
    }
}
