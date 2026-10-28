using Insurance.Helpers;

namespace Authentication.Bff.Api.Services;

public sealed class TokenService
{
    private readonly JwtTokenFactory _factory;

    public TokenService(JwtTokenFactory factory) => _factory = factory;

    public string CreateAccessToken(string userName, IEnumerable<string> roles, IEnumerable<string> permissions) =>
        _factory.CreateAccessToken(userName, roles, permissions);

    public string CreateRefreshToken() => TokenHash.NewOpaqueToken();
    public string CreateSignalRToken(string userName, IEnumerable<string> roles, IEnumerable<string> permissions) =>
    _factory.CreateSignalRToken(userName, roles, permissions);
}
