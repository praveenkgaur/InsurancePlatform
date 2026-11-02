using Authentication.Bff.Api.Services;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("auth/entra")]
public sealed class EntraAuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly LoginService _login;

    public EntraAuthController(IConfiguration config, LoginService login)
    {
        _config = config;
        _login = login;
    }

    /// <summary>Starts OAuth: 302 to Microsoft. Callback is middleware path /signin-oidc (not this controller).</summary>
    [AllowAnonymous]
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!_config.GetValue("UseEntra", false))
            return NotFound(new { message = "Entra login is off (UseEntra)." });

        var ui = (_config["UiOrigin"] ?? "http://localhost:5280").TrimEnd('/');
        var redirect = string.IsNullOrWhiteSpace(returnUrl) ? ui + "/" : returnUrl;
        return Challenge(new AuthenticationProperties { RedirectUri = redirect }, "Entra");
    }

    /// <summary>Clears AppSession and signs out of Entra. Entra then hits middleware /signout-oidc.</summary>
    [AllowAnonymous]
    [HttpGet("logout")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (TryReadSession(out var sessionId, out _))
            await _login.SignOutAsync(sessionId);

        Response.Cookies.Delete(CookieSettings.Name(Request.IsHttps), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        });

        var ui = (_config["UiOrigin"] ?? "http://localhost:5280").TrimEnd('/');
        if (!_config.GetValue("UseEntra", false))
            return Redirect(ui + "/login");

        return SignOut(new AuthenticationProperties { RedirectUri = ui + "/login" }, "EntraCookie", "Entra");
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
