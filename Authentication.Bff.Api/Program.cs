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
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<NotificationPublisher>();
builder.Services.AddScoped<Claims.Api.Services.ClaimsService>();
builder.Services.AddAuthorization();

// Session cookie is always the app login. OIDC is extra; UseEntra only changes DefaultChallengeScheme.
// Do not add a second AddAuthentication(SessionCookie) — that throws "scheme already exists".
// /signin-oidc and /signout-oidc are handled by OpenIdConnect middleware (not MVC controllers).
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = SessionCookieAuthHandler.SchemeName;
        options.DefaultChallengeScheme = builder.Configuration.GetValue("UseEntra", false)
            ? "Entra"
            : SessionCookieAuthHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, SessionCookieAuthHandler>(
        SessionCookieAuthHandler.SchemeName, _ => { })
    .AddCookie("EntraCookie")
    .AddOpenIdConnect("Entra", options =>
    {
        var tenant = builder.Configuration["Entra:TenantId"] ?? "";
        var instance = (builder.Configuration["Entra:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
        options.Authority = $"{instance}/{tenant}/v2.0";
        options.ClientId = builder.Configuration["Entra:ClientId"];
        options.ClientSecret = builder.Configuration["Entra:ClientSecret"];
        options.CallbackPath = builder.Configuration["Entra:CallbackPath"] ?? "/signin-oidc";
        options.SignedOutCallbackPath = builder.Configuration["Entra:SignedOutCallbackPath"] ?? "/signout-oidc";
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.UsePkce = true;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.SignInScheme = "EntraCookie";
        var local = builder.Environment.IsDevelopment();
        options.CorrelationCookie.IsEssential = true;
        options.NonceCookie.IsEssential = true;
        options.CorrelationCookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.NonceCookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        // Unspecified so HTTP localhost still sends the cookie on the Microsoft callback.
        options.CorrelationCookie.SameSite = local ? SameSiteMode.Unspecified : SameSiteMode.None;
        options.NonceCookie.SameSite = local ? SameSiteMode.Unspecified : SameSiteMode.None;
        options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                ctx.ProtocolMessage.ResponseMode = "query";
                return Task.CompletedTask;
            },
            OnRemoteFailure = ctx =>
            {
                var ui = (builder.Configuration["UiOrigin"] ?? "http://localhost:5280").TrimEnd('/');
                var detail = ctx.Failure?.InnerException?.Message ?? ctx.Failure?.Message ?? "remote login failed";
                ctx.Response.Redirect($"{ui}/login?entra=error&msg={Uri.EscapeDataString(detail)}");
                ctx.HandleResponse();
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var login = ctx.HttpContext.RequestServices.GetRequiredService<LoginService>();
                var oid = ctx.Principal?.FindFirst("oid")?.Value
                    ?? ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
                var email = ctx.Principal?.FindFirst("preferred_username")?.Value
                    ?? ctx.Principal?.FindFirst("email")?.Value
                    ?? ctx.Principal?.Identity?.Name;
                var displayName = ctx.Principal?.FindFirst("name")?.Value;
                var signed = await login.SignInFromEntraAsync(oid, email, displayName);
                if (signed is null)
                {
                    ctx.Response.Redirect((builder.Configuration["UiOrigin"] ?? "http://localhost:5280").TrimEnd('/') + "/login?entra=denied");
                    ctx.HandleResponse();
                    return;
                }

                var https = ctx.Request.IsHttps;
                var cookieName = CookieSettings.Name(https);
                ctx.Response.Cookies.Append(
                    cookieName,
                    $"{signed.Value.SessionId:N}.{signed.Value.RawToken}",
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = https,
                        SameSite = https ? SameSiteMode.None : SameSiteMode.Lax,
                        Path = "/",
                        Expires = DateTimeOffset.UtcNow.AddHours(8)
                    });

                var ui = (builder.Configuration["UiOrigin"] ?? "http://localhost:5280").TrimEnd('/');
                ctx.Response.Redirect(ui + "/login?entra=ok");
                ctx.HandleResponse();
            }
        };
    });



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

