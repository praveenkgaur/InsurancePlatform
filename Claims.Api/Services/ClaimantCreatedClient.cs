using System.Net.Http.Json;
using Insurance.Contracts.Claims;

namespace Claims.Api.Services;

/// <summary>POSTs claimant-created JSON to the HTTP Function. Never throws; never holds UI keys.</summary>
public sealed class ClaimantCreatedClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaimantCreatedClient> _log;

    public ClaimantCreatedClient(HttpClient http, IConfiguration config, ILogger<ClaimantCreatedClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task NotifyAsync(InsuranceClaimantDto dto)
    {
        if (!_config.GetValue("Functions:Enabled", false))
            return;

        var url = _config["Functions:ClaimantCreatedUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            _log.LogWarning("ClaimantCreated hook skipped: Functions:Enabled is true but ClaimantCreatedUrl is empty.");
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            var key = _config["Functions:HostKey"]?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.TryAddWithoutValidation("x-functions-key", key);

            request.Content = JsonContent.Create(ClaimantCreatedHookRequest.From(dto));
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "ClaimantCreated hook HTTP {Status} ClaimantId={ClaimantId}",
                    (int)response.StatusCode, dto.ClaimantId);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ClaimantCreated hook failed ClaimantId={ClaimantId}", dto.ClaimantId);
        }
    }
}
