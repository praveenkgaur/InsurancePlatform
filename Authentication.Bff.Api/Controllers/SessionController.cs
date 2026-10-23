using Insurance.Contracts.Auth;
using Insurance.Helpers;
using Microsoft.AspNetCore.Mvc;
using Authentication.Bff.Api.Services;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class SessionController : ControllerBase
{
    private readonly LoginService _login;

    public SessionController(LoginService login) => _login = login;

    [HttpGet("me")]
    public async Task<ActionResult<SessionUserDto>> Me()
    {
        if (!TryReadSession(out var sessionId, out var rawToken))
            return Unauthorized();
        var user = await _login.GetCurrentAsync(sessionId, rawToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    private bool TryReadSession(out Guid sessionId, out string rawToken)
    {
        sessionId = Guid.Empty;
        rawToken = "";
        if (!Request.Cookies.TryGetValue(CookieSettings.Name(Request.IsHttps), out var value))
            return false;
        var parts = value.Split('.', 2);
        return parts.Length == 2 && Guid.TryParse(parts[0], out sessionId) && !string.IsNullOrWhiteSpace(rawToken = parts[1]);
    }
}
