using Authentication.Bff.Api.Services;
using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("auth/users")]
public sealed class UsersController : ControllerBase
{
    private readonly UserAdminService _users;

    public UsersController(UserAdminService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserAdminDto>>> List() =>
        Ok(await _users.ListAsync());

    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<string>>> Roles() =>
        Ok(await _users.ListRolesAsync());

    [HttpPost]
    public async Task<ActionResult<CreateUserResult>> Create([FromBody] CreateUserRequest request)
    {
        var (created, error) = await _users.CreateAsync(request ?? new CreateUserRequest());
        if (error is not null)
            return BadRequest(new ApiMessage { Message = error });
        return Ok(created);
    }
}
