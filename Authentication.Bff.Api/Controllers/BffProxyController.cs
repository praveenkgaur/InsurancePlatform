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

        var target = $"{ResolveDownstream(path)}{Request.QueryString}";
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
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = $"Downstream unreachable ({target}). {ex.Message}" });
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                await TryPublishEventAsync(path, Request.Method, body);

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
    }

    private string ResolveDownstream(string path)
    {
        var useGateway = _config.GetValue("UseGateway", false);
        if (useGateway)
        {
            var gateway = (_config["GatewayBaseUrl"] ?? "http://localhost:5282").TrimEnd('/');
            return $"{gateway}/api/{path}";
        }

        var root = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var host = root.Equals("policies", StringComparison.OrdinalIgnoreCase)
            ? _config["Downstream:Policies"] ?? "http://localhost:5284"
            : root.Equals("billing", StringComparison.OrdinalIgnoreCase)
                ? _config["Downstream:Billing"] ?? "http://localhost:5285"
                : _config["Downstream:Claims"] ?? "http://localhost:5283";
        return $"{host.TrimEnd('/')}/api/{path}";
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
    private async Task TryPublishEventAsync(string path, string method, string body)
    {
        if (HttpMethods.IsGet(method))
            return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        var root = parts[0];
        InsuranceNotificationDto? dto = null;
        if (root.Equals("claims", StringComparison.OrdinalIgnoreCase))
            dto = ClaimEvent(parts, method, body);
        else if (root.Equals("claimants", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method) && parts.Length == 1)
            dto = NewEvent("ClaimantCreated", "InsuranceClaimant", JsonProp(body, "claimantId") ?? JsonProp(body, "fullName") ?? "",
                "Claimant added", $"Claimant {JsonProp(body, "fullName") ?? "record"} was added.");
        else if (root.Equals("policies", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method) && parts.Length == 1)
            dto = NewEvent("PolicyCreated", "InsurancePolicy", JsonProp(body, "policyNumber") ?? "",
                "Policy added", $"Policy {JsonProp(body, "policyNumber") ?? "record"} was created for {JsonProp(body, "holderName") ?? "the holder"}.");

        if (dto is null)
            return;
        dto.TargetRole = "Admin";
        await _publisher.PublishAsync(dto);
    }

    private static InsuranceNotificationDto? ClaimEvent(string[] parts, string method, string body)
    {
        if (HttpMethods.IsPost(method) && parts.Length == 1)
            return NewEvent("ClaimCreated", "InsuranceClaim", JsonProp(body, "claimId") ?? JsonProp(body, "claimNumber") ?? "",
                "Claim added", $"Claim {JsonProp(body, "claimNumber") ?? "record"} was created for {JsonProp(body, "claimantName") ?? "a claimant"}.");

        if (parts.Length < 2)
            return null;

        if (HttpMethods.IsDelete(method))
            return NewEvent("ClaimDeleted", "InsuranceClaim", parts[1], "Claim deleted", $"Claim {parts[1]} was deleted.");

        if (parts.Length >= 3 && parts[2].Equals("approve", StringComparison.OrdinalIgnoreCase))
            return NewEvent("ClaimUpdated", "InsuranceClaim", parts[1], "Claim approved",
                $"Claim {JsonProp(body, "claimNumber") ?? parts[1]} was approved.");

        return NewEvent("ClaimUpdated", "InsuranceClaim", parts[1], "Claim updated", $"Claim {parts[1]} was updated ({method} {string.Join('/', parts)}).");
    }

    private static InsuranceNotificationDto NewEvent(string eventType, string entity, string entityId, string title, string message) => new()
    {
        EventType = eventType,
        EntityName = entity,
        EntityId = entityId,
        Title = title,
        Message = message
    };

    private static string? JsonProp(string body, string name)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind is System.Text.Json.JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        return null;
    }

}
