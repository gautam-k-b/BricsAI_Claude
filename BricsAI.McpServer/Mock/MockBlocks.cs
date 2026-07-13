using System;

namespace BricsAI.McpServer.Mock
{
    /// <summary>
    /// Minimal stub for the Blocks collection. The real plugins only touch this as a secondary
    /// safety-net pass (e.g. catching viewport/paper-space entities LAYDEL misses) on top of the
    /// primary ssget-based pass, which the mock's LISP interpreter already handles — so an empty
    /// block table is sufficient for the mock's purposes.
    /// </summary>
    public class MockBlocks
    {
        public int Count => 0;

        public object Item(int index) => throw new IndexOutOfRangeException("Mock block table is empty.");
    }
}
