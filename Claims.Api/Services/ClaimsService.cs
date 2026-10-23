using Insurance.Contracts.Claims;
using Insurance.Helpers;

namespace Claims.Api.Services;

public sealed class ClaimsService
{
    private readonly SqlCommandExecutor _sql;

    public ClaimsService(SqlCommandExecutor sql) => _sql = sql;

    public Task<IReadOnlyList<InsuranceClaimDto>> GetAllAsync() =>
        _sql.QueryAsync<InsuranceClaimDto>(
            """
            SELECT ClaimId, ClaimNumber, PolicyNumber, ClaimantName, Amount, Status
            FROM dbo.InsuranceClaims
            ORDER BY ClaimId DESC
            """);

    public async Task<InsuranceClaimDto?> ApproveAsync(int claimId)
    {
        var updated = await _sql.ExecuteAsync(
            "UPDATE dbo.InsuranceClaims SET Status = 'Approved' WHERE ClaimId = @ClaimId AND Status = 'Open'",
            new { ClaimId = claimId });
        if (updated == 0)
            return null;
        return await _sql.QuerySingleAsync<InsuranceClaimDto>(
            "SELECT ClaimId, ClaimNumber, PolicyNumber, ClaimantName, Amount, Status FROM dbo.InsuranceClaims WHERE ClaimId = @ClaimId",
            new { ClaimId = claimId });
    }

    public Task<int> DeleteAsync(int claimId) =>
        _sql.ExecuteAsync("DELETE FROM dbo.InsuranceClaims WHERE ClaimId = @ClaimId", new { ClaimId = claimId });
}
