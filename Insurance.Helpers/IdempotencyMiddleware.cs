using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Insurance.Helpers
{
    public sealed class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;

        public IdempotencyMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext ctx, IdempotencyStore store, IConfiguration config)
        {
            if (!config.GetValue("Resilience:Idempotency:Enabled", true)
                || !HttpMethods.IsPost(ctx.Request.Method))
            {
                await _next(ctx);
                return;
            }

            var path = ctx.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/claimants", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/claims", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/api/policies", StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            var header = config["Resilience:Idempotency:HeaderName"] ?? "Idempotency-Key";
            if (!ctx.Request.Headers.TryGetValue(header, out var keyValues))
            {
                await _next(ctx);
                return;
            }

            var key = keyValues.ToString().Trim();
            if (key.Length is 0 or > 64)
            {
                await _next(ctx);
                return;
            }

            ctx.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
            var hash = IdempotencyStore.HashBody(body);

            var existing = await store.GetAsync(key);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                    await ctx.Response.WriteAsJsonAsync(new { message = "Idempotency-Key was reused with a different body." });
                    return;
                }

                ctx.Response.StatusCode = existing.StatusCode;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(existing.ResponseBody ?? "");
                return;
            }

            var original = ctx.Response.Body;
            await using var buffer = new MemoryStream();
            ctx.Response.Body = buffer;
            await _next(ctx);
            buffer.Position = 0;
            var responseText = await new StreamReader(buffer).ReadToEndAsync();
            buffer.Position = 0;
            await buffer.CopyToAsync(original);
            ctx.Response.Body = original;

            if (ctx.Response.StatusCode is >= 200 and < 300)
            {
                await store.SaveAsync(new IdempotencyRow
                {
                    IdempotencyKey = key,
                    HttpMethod = ctx.Request.Method,
                    RequestPath = path,
                    RequestHash = hash,
                    StatusCode = ctx.Response.StatusCode,
                    ResponseBody = responseText
                });
            }
        }
    }
}
