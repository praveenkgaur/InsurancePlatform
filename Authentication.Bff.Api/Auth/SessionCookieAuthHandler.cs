using System.Security.Claims;
using System.Text.Encodings.Web;
using Authentication.Bff.Api.Services;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Authentication.Bff.Api.Auth;

public sealed class SessionCookieAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SessionCookie";
    private readonly LoginService _login;

    public SessionCookieAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        LoginService login)
        : base(options, logger, encoder)
    {
        _login = login;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Same cookie AuthController uses. WASM cannot read HttpOnly; browser sends it.
        if (!SessionCookie.TryRead(Request, out var sessionId, out var rawToken))
            return AuthenticateResult.NoResult();

        var user = await _login.GetCurrentAsync(sessionId, rawToken);
        if (user is null)
            return AuthenticateResult.Fail("Invalid session.");

        var claims = new List<Claim>
        {
            // Clients.User(...) uses NameIdentifier by default.
            new(ClaimTypes.NameIdentifier, user.UserName),
            new(ClaimTypes.Name, user.UserName)
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}