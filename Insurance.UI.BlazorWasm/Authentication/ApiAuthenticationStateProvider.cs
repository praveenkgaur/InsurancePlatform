using System.Security.Claims;
using Insurance.Contracts.Auth;
using Insurance.UI.BlazorWasm.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Insurance.UI.BlazorWasm.Authentication;

public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _auth;
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public ApiAuthenticationStateProvider(AuthService auth) => _auth = auth;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var user = await _auth.GetCurrentAsync();
            return user is null ? Anonymous : CreateState(user);
        }
        catch
        {
            return Anonymous;
        }
    }

    public void NotifySignedIn(SessionUserDto user) => NotifyAuthenticationStateChanged(Task.FromResult(CreateState(user)));

    public void NotifySignedOut() => NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));

    private static AuthenticationState CreateState(SessionUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new("displayName", user.DisplayName)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Permissions.Select(permission => new Claim("permission", permission)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "bff-cookie")));
    }
}
