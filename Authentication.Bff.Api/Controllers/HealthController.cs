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
        foreach (var name in new[] { "Claims", "Policies", "Billing" })
        {
            var baseUrl = DownstreamUrls.BaseUrl(_config, name);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                rows.Add(new { name, url = "(missing)", ok = false, error = DownstreamUrls.UseApim(_config) ? "Set Apim__Gateway on the BFF." : $"Set Downstream__{name} on the BFF." });
                continue;
            }

            var health = $"{baseUrl}/health";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, health);
                if (DownstreamUrls.UseApim(_config))
                {
                    var apimKey = _config["Apim:SubscriptionKey"];
                    if (!string.IsNullOrWhiteSpace(apimKey))
                        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apimKey);
                }
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
            useApim = DownstreamUrls.UseApim(_config),
            useGateway = _config.GetValue("UseGateway", false),
            hint = "Set UseApim true/false. Direct hosts are Downstream__*. APIM host is Apim__Gateway plus suffix claims|policies|billing.",
            downstream = rows
        });
    }
}
