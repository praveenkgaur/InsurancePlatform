using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;
using Insurance.Helpers;
using Microsoft.AspNetCore.Mvc;
using Authentication.Bff.Api.Services;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly LoginService _login;

    public AuthController(LoginService login) => _login = login;

    [HttpPost("login")]
    public async Task<ActionResult<SessionUserDto>> Login([FromBody] LoginRequest request)
    {
        var result = await _login.SignInAsync(request);
        if (result is null)
            return Unauthorized(new ApiMessage { Message = "Invalid user name or password." });

        AppendSessionCookie(result.Value.SessionId, result.Value.RawToken);
        return Ok(result.Value.User);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (TryReadSession(out var sessionId, out _))
            await _login.SignOutAsync(sessionId);
        Response.Cookies.Delete(CookieSettings.Name(Request.IsHttps), CookieOptions());
        return Ok(new ApiMessage { Message = "Signed out." });
    }

    private void AppendSessionCookie(Guid sessionId, string rawToken)
    {
        var value = $"{sessionId:N}.{rawToken}";
        Response.Cookies.Append(CookieSettings.Name(Request.IsHttps), value, CookieOptions());
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddHours(8)
    };

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
