using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Insurance.Helpers;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "InsurancePlatform";
    public string Audience { get; set; } = "InsuranceApis";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 30;
    public string SignalRAudience { get; set; } = "InsuranceSignalR";
    public int SignalRTokenMinutes { get; set; } = 5;
}

public sealed class JwtTokenFactory
{
    private readonly JwtOptions _options;

    public JwtTokenFactory(JwtOptions options) => _options = options;

    public string CreateAccessToken(string userName, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userName),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(ClaimTypes.Name, userName)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string CreateSignalRToken(string userName, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var minutes = _options.SignalRTokenMinutes <= 0 ? 5 : _options.SignalRTokenMinutes;
        var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, userName),
        new(ClaimTypes.Name, userName),
        new(JwtRegisteredClaimNames.Sub, userName)
    };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            _options.Issuer,
            string.IsNullOrWhiteSpace(_options.SignalRAudience) ? "InsuranceSignalR" : _options.SignalRAudience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
