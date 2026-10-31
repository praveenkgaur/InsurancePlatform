using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;
using Insurance.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Authentication.Bff.Api.Services;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly LoginService _login;
    private readonly TokenService _tokens;
    private readonly IConfiguration _config;
    public AuthController(LoginService login, TokenService tokens, IConfiguration config)
    {
        _login = login;
        _tokens = tokens;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<ActionResult<SessionUserDto>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _login.SignInAsync(request);
            if (result is null)
                return Unauthorized(new ApiMessage { Message = "Invalid user name or password." });

            AppendSessionCookie(result.Value.SessionId, result.Value.RawToken);
            return Ok(result.Value.User);
        }
        catch (SqlException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiMessage
            {
                Message = $"Database unavailable (SQL {ex.Number}). Set BFF connection string DemoDataBase, allow Azure SQL firewall, and run Sql/001–004."
            });
        }
    }


    [HttpGet("signalr-token")]
    public async Task<ActionResult<SignalRTokenResponse>> SignalRToken()
    {
        if (!_config.GetValue("SignalR:UseAzure", false))
            return NotFound(new ApiMessage { Message = "Azure SignalR is off (SignalR:UseAzure)." });

        if (!TryReadSession(out var sessionId, out var rawToken))
            return Unauthorized();

        var user = await _login.GetCurrentAsync(sessionId, rawToken);
        if (user is null)
            return Unauthorized();

        var minutes = _config.GetValue("Jwt:SignalRTokenMinutes", 5);
        if (minutes <= 0)
            minutes = 5;

        var jwt = _tokens.CreateSignalRToken(user.UserName, user.Roles, user.Permissions);
        return Ok(new SignalRTokenResponse
        {
            AccessToken = jwt,
            ExpiresInSeconds = minutes * 60
        });
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

    private CookieOptions CookieOptions()
    {
        var crossSite = IsCrossSiteRequest();
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = Request.IsHttps && crossSite ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        };
    }

    private bool IsCrossSiteRequest()
    {
        if (!Request.Headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin))
            return false;
        if (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri))
            return false;
        return !string.Equals(originUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase);
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
