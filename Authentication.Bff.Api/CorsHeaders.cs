using Insurance.Helpers;

namespace Authentication.Bff.Api;

internal static class CorsHeaders
{
    public static IApplicationBuilder UseBffCorsHeaders(this IApplicationBuilder app, string uiOrigin)
    {
        return app.Use(async (ctx, next) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            var allowed = CorsOriginRules.IsAllowed(origin, uiOrigin);
            if (allowed)
            {
                ctx.Response.OnStarting(() =>
                {
                    Write(ctx, origin.Trim());
                    return Task.CompletedTask;
                });
            }

            if (allowed && HttpMethods.IsOptions(ctx.Request.Method))
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });
    }

    private static void Write(HttpContext ctx, string origin)
    {
        ctx.Response.Headers.AccessControlAllowOrigin = origin.TrimEnd('/');
        ctx.Response.Headers.AccessControlAllowCredentials = "true";
        ctx.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
        var requested = ctx.Request.Headers.AccessControlRequestHeaders.ToString();
        ctx.Response.Headers.AccessControlAllowHeaders = string.IsNullOrWhiteSpace(requested)
            ? "Content-Type,Authorization"
            : requested;
        ctx.Response.Headers.Append("Vary", "Origin");
    }
}
