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
        public CaseBoardDto? CaseBoard { get; set; }
    }

    public sealed class AgentChatResponse
    {
        public string Mode { get; set; } = "Single";
        public string Reply { get; set; } = "";
        public string[] ToolsCalled { get; set; } = [];
        public CaseBoardDto CaseBoard { get; set; } = new();
        public List<AgentStepDto> Steps { get; set; } = [];
        public AgentIntakeDto Intake { get; set; } = new();
    }

    public sealed class AgentIntakeDto
    {
        public string Step { get; set; } = "Claimant";
        public string Prompt { get; set; } = "";
        public string Watermark { get; set; } = "";
        public string Help { get; set; } = "";
    }

    public sealed class AgentMetaResponse
    {
        public string Mode { get; set; } = "Single";
        public string Orchestrator { get; set; } = "";
        public List<AgentMetaItem> Agents { get; set; } = [];
        public AgentIntakeDto Intake { get; set; } = new();
    }

    public sealed class AgentMetaItem
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    public sealed class AgentStepDto
    {
        public string AgentKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = ""; // skipped | working | done | needs_input | failed
        public string Message { get; set; } = "";
        public string[] Tools { get; set; } = [];
        public string? ToolResultJson { get; set; }
        public DateTime Utc { get; set; }
    }

    public sealed class CaseBoardDto
    {
        public CasePersonDto Claimant { get; set; } = new();
        public CasePolicyDto Policy { get; set; } = new();
        public CaseClaimDto Claim { get; set; } = new();
        public List<string> Missing { get; set; } = [];
        public List<HandoffDto> Handoffs { get; set; } = [];
    }

    public sealed class CasePersonDto
    {
        public int? Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Status { get; set; } = "empty"; // empty | working | created | skipped
    }

    public sealed class CasePolicyDto
    {
        public int? Id { get; set; }
        public string PolicyNumber { get; set; } = "";
        public string HolderName { get; set; } = "";
        public string Product { get; set; } = "";
        public decimal? Premium { get; set; }
        public string Status { get; set; } = "empty";
    }

    public sealed class CaseClaimDto
    {
        public int? Id { get; set; }
        public string ClaimNumber { get; set; } = "";
        public decimal? Amount { get; set; }
        public string Status { get; set; } = "empty";
    }

    public sealed class HandoffDto
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime Utc { get; set; }
    }
}
