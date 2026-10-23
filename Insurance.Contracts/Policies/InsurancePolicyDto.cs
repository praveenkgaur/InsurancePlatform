namespace Insurance.Contracts.Policies;

public sealed class InsurancePolicyDto
{
    public int PolicyId { get; set; }
    public string PolicyNumber { get; set; } = "";
    public string HolderName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Premium { get; set; }
    public string Status { get; set; } = "";
}

public sealed class UpsertPolicyRequest
{
    public string PolicyNumber { get; set; } = "";
    public string HolderName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Premium { get; set; }
    public string Status { get; set; } = "Active";
}
