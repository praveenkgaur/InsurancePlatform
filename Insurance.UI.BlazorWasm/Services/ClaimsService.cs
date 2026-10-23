using System.Net.Http.Json;
using Insurance.Contracts.Claims;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class ClaimsService
{
    private readonly HttpClient _http;

    public ClaimsService(HttpClient http) => _http = http;

    public Task<InsuranceClaimDto[]?> GetAsync() => _http.GetFromJsonAsync<InsuranceClaimDto[]>("api/claims");

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
}
