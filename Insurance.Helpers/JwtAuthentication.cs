using System.Text;
using Insurance.Contracts.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Insurance.Helpers;

public static class JwtAuthentication
{
    public static IServiceCollection AddInsuranceJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        services.AddSingleton(jwt);
        services.AddSingleton(new JwtTokenFactory(jwt));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
                };
            });
        services.AddAuthorization(options =>
        {
            foreach (var key in new[]
                     {
                         PermissionKeys.ClaimsView, PermissionKeys.ClaimsApprove, PermissionKeys.ClaimsDelete,
                         PermissionKeys.PoliciesView, PermissionKeys.PoliciesCreate, PermissionKeys.PoliciesUpdate,
                         PermissionKeys.BillingView
                     })
            {
                options.AddPolicy(key, policy => policy.RequireClaim("permission", key));
            }
        });
        return services;
    }
}
