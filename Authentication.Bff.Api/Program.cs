using Authentication.Bff.Api;
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
builder.Services.AddInsuranceSwagger("Insurance BFF");
var uiOrigin = (builder.Configuration["UiOrigin"] ?? "http://localhost:5280").Trim().TrimEnd('/');
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.SetIsOriginAllowed(origin => CorsOriginRules.IsAllowed(origin, uiOrigin))
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();
app.UseBffCorsHeaders(uiOrigin);
app.UseCors();
app.UseInsuranceSwagger();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<InsuranceClaimHub>("/hubs/notifications").RequireAuthorization();
app.MapFallbackToFile("index.html");
app.Run();

