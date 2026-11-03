using Insurance.Contracts.Claims;
using Insurance.Helpers;

namespace Claims.Api.Services;

public sealed class ClaimsService
{
    private readonly SqlCommandExecutor _sql;
    private readonly ClaimantCreatedClient _claimantCreated;

    public ClaimsService(SqlCommandExecutor sql, ClaimantCreatedClient claimantCreated)
    {
        _sql = sql;
        _claimantCreated = claimantCreated;
    }

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

    private const string ClaimantSelect = """
        SELECT ClaimantId, FullName,
            ISNULL(FirstName, '') AS FirstName, ISNULL(LastName, '') AS LastName,
            DateOfBirth, ISNULL(Gender, '') AS Gender,
            ISNULL(Email, '') AS Email, ISNULL(Phone, '') AS Phone, ISNULL(AlternatePhone, '') AS AlternatePhone,
            ISNULL(AddressLine1, '') AS AddressLine1, ISNULL(AddressLine2, '') AS AddressLine2,
            ISNULL(City, '') AS City, ISNULL(StateName, '') AS StateName, ISNULL(PinCode, '') AS PinCode,
            ISNULL(Country, '') AS Country, ISNULL(IdType, '') AS IdType, ISNULL(IdNumber, '') AS IdNumber,
            ISNULL(Notes, '') AS Notes
        FROM dbo.InsuranceClaimants
        """;

    public Task<IReadOnlyList<InsuranceClaimantDto>> GetClaimantsAsync() =>
        _sql.QueryAsync<InsuranceClaimantDto>(ClaimantSelect + " ORDER BY ClaimantId DESC");

    public async Task<InsuranceClaimantDto> CreateClaimantAsync(CreateClaimantRequest request)
    {
        request.EnsureFullName();
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.InsuranceClaimants
            (
                FullName, FirstName, LastName, DateOfBirth, Gender,
                Email, Phone, AlternatePhone,
                AddressLine1, AddressLine2, City, StateName, PinCode, Country,
                IdType, IdNumber, Notes
            )
            VALUES
            (
                @FullName, @FirstName, @LastName, @DateOfBirth, @Gender,
                @Email, @Phone, @AlternatePhone,
                @AddressLine1, @AddressLine2, @City, @StateName, @PinCode, @Country,
                @IdType, @IdNumber, @Notes
            )
            """,
            request);

        var created = await _sql.QuerySingleAsync<InsuranceClaimantDto>(
            ClaimantSelect + """
             WHERE FullName = @FullName
             ORDER BY ClaimantId DESC
            """,
            new { request.FullName }) ?? throw new InvalidOperationException("Claimant insert did not return a row.");

        await _claimantCreated.NotifyAsync(created);
        return created;
    }
}
