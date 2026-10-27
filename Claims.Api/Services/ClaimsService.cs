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

    public async Task<InsuranceClaimDto> CreateAsync(CreateClaimRequest request)
    {
        var claimantName = request.ClaimantName;
        if (request.ClaimantId is int claimantId)
        {
            var claimant = await _sql.QuerySingleAsync<InsuranceClaimantDto>(
                "SELECT ClaimantId, FullName, Email, Phone FROM dbo.InsuranceClaimants WHERE ClaimantId = @ClaimantId",
                new { ClaimantId = claimantId });
            if (claimant is not null)
                claimantName = claimant.FullName;
        }

        var claimNumber = string.IsNullOrWhiteSpace(request.ClaimNumber)
            ? $"CLM-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : request.ClaimNumber.Trim();

        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.InsuranceClaims (ClaimNumber, PolicyNumber, ClaimantName, Amount, Status)
            VALUES (@ClaimNumber, @PolicyNumber, @ClaimantName, @Amount, 'Open')
            """,
            new
            {
                ClaimNumber = claimNumber,
                request.PolicyNumber,
                ClaimantName = claimantName,
                request.Amount
            });

        return await _sql.QuerySingleAsync<InsuranceClaimDto>(
            """
            SELECT ClaimId, ClaimNumber, PolicyNumber, ClaimantName, Amount, Status
            FROM dbo.InsuranceClaims
            WHERE ClaimNumber = @ClaimNumber
            """,
            new { ClaimNumber = claimNumber }) ?? throw new InvalidOperationException("Claim insert did not return a row.");
    }

    public Task<IReadOnlyList<InsuranceClaimantDto>> GetClaimantsAsync() =>
        _sql.QueryAsync<InsuranceClaimantDto>(
            """
            SELECT ClaimantId, FullName, ISNULL(Email, '') AS Email, ISNULL(Phone, '') AS Phone
            FROM dbo.InsuranceClaimants
            ORDER BY ClaimantId DESC
            """);

    public async Task<InsuranceClaimantDto> CreateClaimantAsync(CreateClaimantRequest request)
    {
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.InsuranceClaimants (FullName, Email, Phone)
            VALUES (@FullName, @Email, @Phone)
            """,
            request);

        return await _sql.QuerySingleAsync<InsuranceClaimantDto>(
            """
            SELECT TOP 1 ClaimantId, FullName, ISNULL(Email, '') AS Email, ISNULL(Phone, '') AS Phone
            FROM dbo.InsuranceClaimants
            WHERE FullName = @FullName
            ORDER BY ClaimantId DESC
            """,
            new { request.FullName }) ?? throw new InvalidOperationException("Claimant insert did not return a row.");
    }
}
