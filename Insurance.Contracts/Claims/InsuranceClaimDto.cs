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

public sealed class CreateClaimRequest
{
    public string ClaimNumber { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public int? ClaimantId { get; set; }
    public string ClaimantName { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class InsuranceClaimantDto
{
    public int ClaimantId { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AlternatePhone { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string City { get; set; } = "";
    public string StateName { get; set; } = "";
    public string PinCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string IdType { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class CreateClaimantRequest
{
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AlternatePhone { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string City { get; set; } = "";
    public string StateName { get; set; } = "";
    public string PinCode { get; set; } = "";
    public string Country { get; set; } = "India";
    public string IdType { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Notes { get; set; } = "";

    public void EnsureFullName()
    {
        if (!string.IsNullOrWhiteSpace(FullName))
            return;
        FullName = $"{FirstName} {LastName}".Trim();
    }
}
