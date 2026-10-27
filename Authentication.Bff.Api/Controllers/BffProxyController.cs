using Authentication.Bff.Api.Services;
using Insurance.Contracts.Notifications;
using Insurance.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class BffProxyController : ControllerBase
{
    private readonly LoginService _login;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly NotificationPublisher _publisher;   // add
    public BffProxyController(LoginService login, IHttpClientFactory http, IConfiguration config, NotificationPublisher publisher)
    {
        _login = login;
        _http = http;
        _config = config;
        _publisher = publisher;
    }

    [HttpGet("{**path}")]
    [HttpPost("{**path}")]
    [HttpPut("{**path}")]
    [HttpDelete("{**path}")]
    public async Task<IActionResult> Forward(string path)
    {
        if (!TryReadSession(out var sessionId, out var rawToken))
            return Unauthorized();
        var accessToken = await _login.GetAccessTokenAsync(sessionId, rawToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return Unauthorized();

        var gateway = _config["GatewayBaseUrl"] ?? "http://localhost:5282";
        var target = $"{gateway}/api/{path}{Request.QueryString}";
        var method = new HttpMethod(Request.Method);
        using var message = new HttpRequestMessage(method, target);
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
       
        if (Request.ContentLength is > 0 || !string.IsNullOrEmpty(Request.ContentType))
        {
            message.Content = new StreamContent(Request.Body);
            if (!string.IsNullOrEmpty(Request.ContentType))
                message.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        var client = _http.CreateClient("Gateway");
        var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();

        if(response.IsSuccessStatusCode)
            await TryPublishClaimEventAsync(path, Request.Method);

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
        };
    }

    private bool TryReadSession(out Guid sessionId, out string rawToken)
    {
        sessionId = Guid.Empty;
        rawToken = "";
        if (!Request.Cookies.TryGetValue(CookieSettings.Name(Request.IsHttps), out var value))
            return false;
        var parts = value.Split('.', 2);
        return parts.Length == 2 && Guid.TryParse(parts[0], out sessionId) && !string.IsNullOrWhiteSpace(rawToken = parts[1]);
    }
    private async Task TryPublishClaimEventAsync(string path, string method)
    {
        // path is like "claims/5/approve" or "claims/5"
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("claims", StringComparison.OrdinalIgnoreCase))
            return;

        var dto = new InsuranceNotificationDto
        {
            EntityName = "InsuranceClaim",
            EntityId = parts[1],
            EventType = method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? "ClaimDeleted" : "ClaimUpdated",
            Title = method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? "Claim deleted" : "Claim updated",
            Message = $"{method} {path}",
            TargetRole = "Admin" // configurable later via SignalR:Rules
                                 // TargetUserName = "Praveen"  // optional private target
        };
        await _publisher.PublishAsync(dto);
    }

}
