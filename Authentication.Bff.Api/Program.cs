using Authentication.Bff.Api;
using Authentication.Bff.Api.Auth;
using Authentication.Bff.Api.Hubs;
using Authentication.Bff.Api.Services;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
{
    var localName = $"appsettings.{builder.Environment.EnvironmentName}.local.json";
    foreach (var dir in new[] { builder.Environment.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Distinct())
    {
        var path = Path.Combine(dir, localName);
        if (File.Exists(path))
            builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: true);
    }
}
if (builder.Environment.IsDevelopment())
    builder.WebHost.UseUrls("http://localhost:5281");
builder.Services.AddInsuranceData(builder.Configuration);
builder.Services.AddInsuranceJwt(builder.Configuration);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<NotificationPublisher>();
builder.Services.AddScoped<Claims.Api.Services.ClaimsService>();
builder.Services.AddAuthentication(SessionCookieAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, SessionCookieAuthHandler>(SessionCookieAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("Gateway", client =>
    client.Timeout = TimeSpan.FromSeconds(builder.Environment.IsDevelopment() ? 3 : 30));
builder.Services.AddHttpClient("AgentClaims");
builder.Services.AddHttpClient("AgentPolicies");
builder.Services.AddHttpClient("AgentPython");
builder.Services.AddSingleton<Authentication.Bff.Api.Agent.RecordGuardrails>();
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<InsuranceClaimHub>("/hubs/notifications").RequireAuthorization();
app.Run();

