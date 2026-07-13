using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsAI.McpServer.Mock
{
    /// <summary>
    /// Mimics the AutoCAD/BricsCAD Layers collection surface used by the plugins:
    /// Count, Item(int) for index-based iteration, Item(string) for name lookup, Add(string).
    /// </summary>
    public class MockLayers
    {
        private readonly List<MockLayer> _layers = new();

        public int Count => _layers.Count;

        public MockLayer Item(int index) => _layers[index];

        public MockLayer Item(string name)
        {
            var layer = _layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (layer == null) throw new InvalidOperationException($"Mock layer '{name}' not found.");
            return layer;
        }

        public MockLayer Add(string name)
        {
            var existing = _layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var layer = new MockLayer(name);
            _layers.Add(layer);
            return layer;
        }

        public bool Remove(string name)
        {
            var layer = _layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (layer == null) return false;
            _layers.Remove(layer);
            return true;
        }
    }
}
