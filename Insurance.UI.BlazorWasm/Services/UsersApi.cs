using System.Net.Http.Json;
using Insurance.Contracts.Auth;
using Insurance.Contracts.Common;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class UsersApi
{
    private readonly HttpClient _http;

    public UsersApi(HttpClient http) => _http = http;

    public Task<UserAdminDto[]?> ListAsync() => _http.GetFromJsonAsync<UserAdminDto[]>("auth/users");

    public Task<string[]?> RolesAsync() => _http.GetFromJsonAsync<string[]>("auth/users/roles");

    public async Task<(CreateUserResult? Created, string? Error)> CreateAsync(CreateUserRequest request)
    {
        var response = await _http.PostAsJsonAsync("auth/users", request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateUserResult>(), null);

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ApiMessage>();
            return (null, body?.Message ?? $"Could not create user (HTTP {(int)response.StatusCode}).");
        }
        catch
        {
            return (null, $"Could not create user (HTTP {(int)response.StatusCode}).");
        }
    }
}
