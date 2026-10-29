using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Insurance.Helpers;

public static class InsuranceSwagger
{
    public static IServiceCollection AddInsuranceSwagger(this IServiceCollection services, string title)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title,
                Version = "v1",
                Description = "Health is anonymous. API routes on Claims/Policies/Billing need JWT Bearer (the BFF sends this; the browser does not)."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT from the BFF session (APIs only). Not used for /health."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });
        return services;
    }

    public static WebApplication UseInsuranceSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", app.Environment.ApplicationName);
        });
        return app;
    }

    public static WebApplication MapInsuranceHealth(this WebApplication app, string serviceName)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName }))
            .AllowAnonymous()
            .WithTags("Health");
        return app;
    }
}
