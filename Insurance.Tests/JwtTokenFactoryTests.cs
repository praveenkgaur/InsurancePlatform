using System.IdentityModel.Tokens.Jwt;
using Insurance.Helpers;

namespace Insurance.Tests;

public sealed class JwtTokenFactoryTests
{
    [Fact]
    public void Access_token_contains_role_and_permission_claims()
    {
        var factory = new JwtTokenFactory(new JwtOptions
        {
            Issuer = "InsurancePlatform",
            Audience = "InsuranceApis",
            SigningKey = "InsurancePlatform-dev-signing-key-32ch"
        });
        var token = factory.CreateAccessToken("Praveen", ["Admin"], ["Claims.Approve"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase) && c.Value == "Admin");
        Assert.Contains(jwt.Claims, c => c.Type == "permission" && c.Value == "Claims.Approve");
    }
}
