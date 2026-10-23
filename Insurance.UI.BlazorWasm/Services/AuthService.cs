using System.Net.Http.Json;
using Insurance.Contracts.Auth;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class AuthService
{
    private readonly HttpClient _http;

    public AuthService(HttpClient http) => _http = http;

    public async Task<SessionUserDto?> LoginAsync(string userName, string password)
    {
        var response = await _http.PostAsJsonAsync("auth/login", new LoginRequest { UserName = userName, Password = password });
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<SessionUserDto>();
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
