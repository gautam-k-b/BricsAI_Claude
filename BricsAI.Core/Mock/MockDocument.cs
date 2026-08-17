using System.Collections.Generic;

namespace BricsAI.Core.Mock
{
    public class MockDocument
    {
        public MockLayers Layers { get; } = new();
        public MockSelectionSets SelectionSets { get; }
        public MockBlocks Blocks { get; } = new();
        public List<MockEntity> Entities { get; } = new();

        /// <summary>
        /// The entities matched by the most recent ssget-style filter parsed out of a SendCommand
        /// string, carried across calls so a later bare verb (FLATTEN, EXPLODE, ERASE with no
        /// filter of its own) can act on the selection built by an earlier sssetfirst call —
        /// mirrors how the real plugins split "select" and "act" across two SendCommand calls.
        /// </summary>
        internal List<MockEntity>? PendingSelection { get; set; }

        public MockDocument()
        {
            SelectionSets = new MockSelectionSets(this);
        }

        public object? SendCommand(string command)
        {
            MockLispInterpreter.Execute(this, command);
            return null;
        }
    }
}
