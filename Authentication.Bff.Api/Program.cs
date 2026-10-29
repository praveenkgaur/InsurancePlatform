using Authentication.Bff.Api.Auth;
using Authentication.Bff.Api.Hubs;
using Authentication.Bff.Api.Services;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInsuranceData(builder.Configuration);
builder.Services.AddInsuranceJwt(builder.Configuration);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<NotificationPublisher>();
builder.Services.AddAuthentication(SessionCookieAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, SessionCookieAuthHandler>(SessionCookieAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("Gateway", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("AgentClaims");
builder.Services.AddHttpClient("AgentPolicies");
builder.Services.AddHttpClient("AgentPython");
builder.Services.AddScoped<Authentication.Bff.Api.Agent.AgentChatService>();

builder.Services.AddControllers();
var uiOrigin = (builder.Configuration["UiOrigin"] ?? "http://localhost:5280").Trim().TrimEnd('/');
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;
            var o = origin.Trim().TrimEnd('/');
            return string.Equals(o, uiOrigin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o, "http://localhost:5280", StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<InsuranceClaimHub>("/hubs/notifications").RequireAuthorization();
app.Run();

