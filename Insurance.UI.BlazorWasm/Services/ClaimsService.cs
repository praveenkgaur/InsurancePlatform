using System.Net.Http.Json;
using Insurance.Contracts.Claims;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class ClaimsService
{
    private readonly HttpClient _http;

    public ClaimsService(HttpClient http) => _http = http;

    public Task<InsuranceClaimDto[]?> GetAsync() => GetArrayAsync<InsuranceClaimDto>("api/claims");

    public async Task<bool> ApproveAsync(int claimId)
    {
        var response = await _http.PostAsJsonAsync($"api/claims/{claimId}/approve", new ApproveClaimRequest());
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(int claimId)
    {
        var response = await _http.DeleteAsync($"api/claims/{claimId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<(bool Ok, string Error)> CreateAsync(CreateClaimRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/claims", request);
        return response.IsSuccessStatusCode
            ? (true, "")
            : (false, await response.Content.ReadAsStringAsync());
    }

    public Task<InsuranceClaimantDto[]?> GetClaimantsAsync() => GetArrayAsync<InsuranceClaimantDto>("api/claimants");

    private async Task<T[]?> GetArrayAsync<T>(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return [];
            return await response.Content.ReadFromJsonAsync<T[]>() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<(bool Ok, string Error)> CreateClaimantAsync(CreateClaimantRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/claimants", request);
        return response.IsSuccessStatusCode
            ? (true, "")
            : (false, await response.Content.ReadAsStringAsync());
    }
}
