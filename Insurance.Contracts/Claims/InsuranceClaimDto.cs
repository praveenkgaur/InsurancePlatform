namespace Insurance.Contracts.Claims;

public sealed class InsuranceClaimDto
{
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public string ClaimantName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}

public sealed class ApproveClaimRequest
{
    public string? Comment { get; set; }
}
