using System.Net.Http.Json;
using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class AuthService
{
    private readonly HttpClient _http;

    public AuthService(HttpClient http) => _http = http;

    public async Task<(SessionUserDto? User, string? Error)> LoginAsync(string userName, string password)
    {
        var response = await _http.PostAsJsonAsync("auth/login", new LoginRequest { UserName = userName, Password = password });
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SessionUserDto>(), null);

        ApiMessage? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<ApiMessage>();
        }
        catch
        {
            // non-JSON 500 from IIS / SQL timeout
        }

        if ((int)response.StatusCode >= 500)
            return (null, string.IsNullOrWhiteSpace(body?.Message)
                ? "Login failed: database or server error (HTTP 500). Check Azure SQL on pk-ins-bff."
                : body!.Message);

        return (null, string.IsNullOrWhiteSpace(body?.Message) ? "Invalid user name or password." : body!.Message);
    }

    public async Task<SessionUserDto?> GetCurrentAsync()
    {
        var response = await _http.GetAsync("auth/me");
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<SessionUserDto>();
    }

    public Task LogoutAsync() => _http.PostAsync("auth/logout", null);
}
