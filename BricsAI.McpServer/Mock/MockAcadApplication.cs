namespace BricsAI.McpServer.Mock
{
    public class MockAcadApplication
    {
        public MockDocument ActiveDocument { get; } = new();
        public string Version => "24.2.06 (Mock)";
    }
}
