using Insurance.Contracts.Security;
using Insurance.UI.BlazorWasm;
using Insurance.UI.BlazorWasm.Authentication;
using Insurance.UI.BlazorWasm.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var bff = builder.Configuration["BffBaseUrl"] ?? "http://localhost:5281";
builder.Services.AddTransient<CookieHandler>();
builder.Services.AddHttpClient("Bff", client => client.BaseAddress = new Uri(bff))
    .AddHttpMessageHandler<CookieHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Bff"));
builder.Services.AddAuthorizationCore(options =>
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
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ClaimsService>();
builder.Services.AddScoped<PoliciesService>();
builder.Services.AddScoped<AuthenticationStateProvider, ApiAuthenticationStateProvider>();

await builder.Build().RunAsync();
