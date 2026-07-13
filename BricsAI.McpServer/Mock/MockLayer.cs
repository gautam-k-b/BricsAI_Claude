namespace BricsAI.McpServer.Mock
{
    public class MockLayer
    {
        public string Name { get; set; }
        public bool Lock { get; set; }
        public bool Freeze { get; set; }
        public bool LayerOn { get; set; } = true;

        public MockLayer(string name)
        {
            Name = name;
        }
    }
}
