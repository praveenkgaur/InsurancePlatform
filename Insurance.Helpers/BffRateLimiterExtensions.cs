using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Insurance.Helpers
{
    public static class BffRateLimiterExtensions
    {
        public static IServiceCollection AddInsuranceRateLimiter(this IServiceCollection services, IConfiguration config)
        {
            if (!config.GetValue("Resilience:RateLimit:Enabled", true))
                return services;

            var writeLimit = config.GetValue("Resilience:RateLimit:PermitLimit", 30);
            var window = TimeSpan.FromSeconds(config.GetValue("Resilience:RateLimit:WindowSeconds", 60));

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (ctx, token) =>
                {
                    ctx.HttpContext.Response.ContentType = "application/json";
                    await ctx.HttpContext.Response.WriteAsync(
                        """{"message":"Too many requests. Wait a minute and try again."}""", token);
                };
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                {
                    var path = http.Request.Path.Value ?? "";
                    if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                        return RateLimitPartition.GetNoLimiter("health");

                    var cookie = CookieSettings.Name(http.Request.IsHttps);
                    var user = (http.Request.Cookies.TryGetValue(cookie, out var v) ? v : null)
                        ?? http.Connection.RemoteIpAddress?.ToString()
                        ?? "anon";
                    var write = HttpMethods.IsPost(http.Request.Method)
                        || HttpMethods.IsPut(http.Request.Method)
                        || HttpMethods.IsDelete(http.Request.Method);
                    var partition = write ? user + ":w" : user + ":r";
                    var permit = write ? writeLimit : writeLimit * 4;
                    return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(5, permit),
                        Window = window,
                        QueueLimit = 0
                    });
                });
            });
            return services;
        }
    }
}
