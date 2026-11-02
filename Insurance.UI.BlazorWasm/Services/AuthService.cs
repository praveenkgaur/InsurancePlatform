using System.Net.Http.Json;
using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;
using Microsoft.AspNetCore.Components;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly NavigationManager _nav;

    public AuthService(HttpClient http, IConfiguration config, NavigationManager nav)
    {
        _http = http;
        _config = config;
        _nav = nav;
    }

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

    public async Task LogoutAsync()
    {
        if (_config.GetValue("UseEntra", false))
        {
            var bff = (_config.GetValue("UseApim", false)
                    ? _config["ApimBffBaseUrl"]
                    : _config["BffBaseUrl"])
                ?? "http://localhost:5281";
            _nav.NavigateTo($"{bff.TrimEnd('/')}/auth/entra/logout", forceLoad: true);
            return;
        }

        await _http.PostAsync("auth/logout", null);
    }
}
