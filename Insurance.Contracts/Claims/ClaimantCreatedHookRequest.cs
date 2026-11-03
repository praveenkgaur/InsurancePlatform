namespace Insurance.Contracts.Claims;

/// <summary>Payload the HTTP Function receives. No ID number in v1.</summary>
public sealed class ClaimantCreatedHookRequest
{
    public int ClaimantId { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string City { get; set; } = "";
    public string PinCode { get; set; } = "";

    public static ClaimantCreatedHookRequest From(InsuranceClaimantDto dto) => new()
    {
        ClaimantId = dto.ClaimantId,
        FullName = dto.FullName ?? "",
        FirstName = dto.FirstName ?? "",
        LastName = dto.LastName ?? "",
        Email = dto.Email ?? "",
        Phone = dto.Phone ?? "",
        City = dto.City ?? "",
        PinCode = dto.PinCode ?? ""
    };
}
