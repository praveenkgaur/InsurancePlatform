namespace Insurance.Contracts.Agent
{
    public sealed class AgentChatTurn
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public sealed class AgentChatRequest
    {
        public string Message { get; set; } = "";
        public List<AgentChatTurn> History { get; set; } = [];
    }

    public sealed class AgentChatResponse
    {
        public string Reply { get; set; } = "";
        public string[] ToolsCalled { get; set; } = [];
    }
}
