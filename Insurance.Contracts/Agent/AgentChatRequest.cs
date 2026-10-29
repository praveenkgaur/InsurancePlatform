namespace Insurance.Contracts.Agent
{
    public sealed class AgentChatRequest
    {
        public string Message { get; set; } = "";
    }

    public sealed class AgentChatResponse
    {
        public string Reply { get; set; } = "";
        public string[] ToolsCalled { get; set; } = [];
    }
}
