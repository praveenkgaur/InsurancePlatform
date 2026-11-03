using Insurance.Contracts.Claims;

namespace Insurance.Tests;

public sealed class ClaimantContractTests
{
    [Fact]
    public void EnsureFullName_fills_from_first_and_last()
    {
        var request = new CreateClaimantRequest { FirstName = "Maya", LastName = "Patel" };
        request.EnsureFullName();
        Assert.Equal("Maya Patel", request.FullName);
    }

    [Fact]
    public void Hook_request_omits_id_number()
    {
        var dto = new InsuranceClaimantDto
        {
            ClaimantId = 9,
            FullName = "Maya Patel",
            FirstName = "Maya",
            LastName = "Patel",
            Email = "maya@test.local",
            Phone = "999",
            City = "Pune",
            PinCode = "411001",
            IdNumber = "secret"
        };
        var hook = ClaimantCreatedHookRequest.From(dto);
        Assert.Equal(9, hook.ClaimantId);
        Assert.Equal("Pune", hook.City);
        Assert.Equal("411001", hook.PinCode);
        Assert.Null(typeof(ClaimantCreatedHookRequest).GetProperty("IdNumber"));
    }
}
