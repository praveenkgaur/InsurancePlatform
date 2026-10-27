using Insurance.Contracts.Claims;
using Insurance.Contracts.Common;
using Insurance.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Claims.Api.Services;

namespace Claims.Api.Controllers;

[ApiController]
[Route("api/claimants")]
[Authorize]
public sealed class ClaimantsController : ControllerBase
{
    private readonly ClaimsService _claims;

    public ClaimantsController(ClaimsService claims) => _claims = claims;

    [HttpGet]
    [Authorize(Policy = PermissionKeys.ClaimsView)]
    public Task<IReadOnlyList<InsuranceClaimantDto>> Get() => _claims.GetClaimantsAsync();

    [HttpPost]
    [Authorize(Policy = PermissionKeys.ClaimsView)]
    public async Task<ActionResult<InsuranceClaimantDto>> Create([FromBody] CreateClaimantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new ApiMessage { Message = "Full name is required." });

        var created = await _claims.CreateClaimantAsync(request);
        return Ok(created);
    }
}
