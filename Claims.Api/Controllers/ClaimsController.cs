using Insurance.Contracts.Claims;
using Insurance.Contracts.Common;
using Insurance.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Claims.Api.Services;

namespace Claims.Api.Controllers;

[ApiController]
[Route("api/claims")]
[Authorize]
public sealed class ClaimsController : ControllerBase
{
    private readonly ClaimsService _claims;

    public ClaimsController(ClaimsService claims) => _claims = claims;

    [HttpGet]
    [Authorize(Policy = PermissionKeys.ClaimsView)]
    public Task<IReadOnlyList<InsuranceClaimDto>> Get() => _claims.GetAllAsync();

    [HttpPost("{claimId:int}/approve")]
    [Authorize(Policy = PermissionKeys.ClaimsApprove)]
    public async Task<ActionResult<InsuranceClaimDto>> Approve(int claimId, [FromBody] ApproveClaimRequest? request)
    {
        _ = request;
        var claim = await _claims.ApproveAsync(claimId);
        return claim is null
            ? BadRequest(new ApiMessage { Message = "Claim was not found or is not open." })
            : Ok(claim);
    }

    [HttpDelete("{claimId:int}")]
    [Authorize(Policy = PermissionKeys.ClaimsDelete)]
    public async Task<IActionResult> Delete(int claimId)
    {
        var deleted = await _claims.DeleteAsync(claimId);
        return deleted == 0 ? NotFound() : NoContent();
    }
}
