using Insurance.Contracts.Common;
using Insurance.Contracts.Policies;
using Insurance.Contracts.Security;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Policies.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize]
public sealed class PoliciesController : ControllerBase
{
    private readonly SqlCommandExecutor _sql;

    public PoliciesController(SqlCommandExecutor sql) => _sql = sql;

    [HttpGet]
    [Authorize(Policy = PermissionKeys.PoliciesView)]
    public Task<IReadOnlyList<InsurancePolicyDto>> Get() =>
        _sql.QueryAsync<InsurancePolicyDto>(
            """
            SELECT PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status
            FROM dbo.InsurancePolicies
            ORDER BY PolicyId DESC
            """);

    [HttpPost]
    [Authorize(Policy = PermissionKeys.PoliciesCreate)]
    public async Task<ActionResult<InsurancePolicyDto>> Create([FromBody] UpsertPolicyRequest request)
    {
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.InsurancePolicies (PolicyNumber, HolderName, ProductName, Premium, Status)
            VALUES (@PolicyNumber, @HolderName, @ProductName, @Premium, @Status)
            """,
            request);
        var created = await _sql.QuerySingleAsync<InsurancePolicyDto>(
            "SELECT TOP 1 PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status FROM dbo.InsurancePolicies WHERE PolicyNumber = @PolicyNumber",
            new { request.PolicyNumber });
        return Ok(created);
    }

    [HttpPut("{policyId:int}")]
    [Authorize(Policy = PermissionKeys.PoliciesUpdate)]
    public async Task<ActionResult<InsurancePolicyDto>> Update(int policyId, [FromBody] UpsertPolicyRequest request)
    {
        var updated = await _sql.ExecuteAsync(
            """
            UPDATE dbo.InsurancePolicies
            SET HolderName = @HolderName, ProductName = @ProductName, Premium = @Premium, Status = @Status
            WHERE PolicyId = @PolicyId
            """,
            new { PolicyId = policyId, request.HolderName, request.ProductName, request.Premium, request.Status });
        if (updated == 0)
            return NotFound(new ApiMessage { Message = "Policy was not found." });
        var row = await _sql.QuerySingleAsync<InsurancePolicyDto>(
            "SELECT PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status FROM dbo.InsurancePolicies WHERE PolicyId = @PolicyId",
            new { PolicyId = policyId });
        return Ok(row);
    }
}
