using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    public HealthController(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    [HttpGet("/health")]
    public IActionResult Live() => Ok(new { status = "ok", service = "bff" });

    [HttpGet("/health/downstream")]
    public async Task<IActionResult> Downstream(CancellationToken cancellationToken)
    {
        var client = _http.CreateClient("Gateway");
        var rows = new List<object>();
        foreach (var (name, key) in new[]
                 {
                     ("Claims", "Downstream:Claims"),
                     ("Policies", "Downstream:Policies"),
                     ("Billing", "Downstream:Billing")
                 })
        {
            var baseUrl = (_config[key] ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                rows.Add(new { name, url = "(missing)", ok = false, error = $"Set {key.Replace(":", "__")} on the BFF (long Default domain, not *.azurewebsites.net without stamp)." });
                continue;
            }

            var health = $"{baseUrl}/health";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, health);
                var apimKey = _config["Apim:SubscriptionKey"];
                if (!string.IsNullOrWhiteSpace(apimKey))
                    request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apimKey);
                using var response = await client.SendAsync(request, cancellationToken);
                rows.Add(new { name, url = baseUrl, health, ok = response.IsSuccessStatusCode, status = (int)response.StatusCode });
            }
            catch (Exception ex)
            {
                rows.Add(new { name, url = baseUrl, health, ok = false, error = ex.Message });
            }
        }

        return Ok(new
        {
            useGateway = _config.GetValue("UseGateway", false),
            hint = "ok:false with a short *.azurewebsites.net host means copy Overview Default domain into Downstream__Claims / Policies / Billing.",
            downstream = rows
        });
    }
}
