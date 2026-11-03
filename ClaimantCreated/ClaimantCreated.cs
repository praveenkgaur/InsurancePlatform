using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ClaimantCreated;

public class ClaimantCreated
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<ClaimantCreated> _logger;
    private readonly FunctionClaimantLogWriter _logWriter;

    public ClaimantCreated(ILogger<ClaimantCreated> logger, FunctionClaimantLogWriter logWriter)
    {
        _logger = logger;
        _logWriter = logWriter;
    }

    [Function("ClaimantCreated")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        if (HttpMethods.IsGet(req.Method))
            return new OkObjectResult(new { ok = true, message = "POST JSON claimantId, fullName, firstName, lastName, email, phone, city, pinCode." });

        ClaimantCreatedBody? body;
        string? raw = null;
        try
        {
            using var reader = new StreamReader(req.Body);
            raw = await reader.ReadToEndAsync();
            body = JsonSerializer.Deserialize<ClaimantCreatedBody>(raw, Json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON");
            await _logWriter.WriteAsync("POST", null, null, null, null, null, 400, false, "Invalid JSON.", Truncate(raw));
            return new BadRequestObjectResult(new { ok = false, message = "Invalid JSON." });
        }

        if (body is null || body.ClaimantId <= 0)
        {
            await _logWriter.WriteAsync("POST", body?.ClaimantId, body?.FullName, body?.City, body?.PinCode, body?.Email, 400, false, "claimantId is required.", Truncate(raw));
            return new BadRequestObjectResult(new { ok = false, message = "claimantId is required." });
        }

        _logger.LogInformation(
            "ClaimantCreated hook ClaimantId={ClaimantId} FullName={FullName} City={City} PinCode={PinCode}",
            body.ClaimantId, body.FullName, body.City, body.PinCode);

        var logged = await _logWriter.WriteAsync(
            "POST", body.ClaimantId, body.FullName, body.City, body.PinCode, body.Email,
            200, true, "ok", Truncate(raw));

        return new OkObjectResult(new { ok = true, body.ClaimantId, logged });
    }

    private static string? Truncate(string? json) =>
        string.IsNullOrEmpty(json) ? json : json.Length <= 4000 ? json : json[..4000];
}

public sealed class ClaimantCreatedBody
{
    public int ClaimantId { get; set; }
    public string FullName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string City { get; set; } = "";
    public string PinCode { get; set; } = "";
}
