using System.Net.Http.Json;
using Insurance.Contracts.Policies;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class PoliciesService
{
    private readonly HttpClient _http;

    public PoliciesService(HttpClient http) => _http = http;

    public Task<InsurancePolicyDto[]?> GetAsync() => _http.GetFromJsonAsync<InsurancePolicyDto[]>("api/policies");

    public async Task<bool> CreateAsync(UpsertPolicyRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/policies", request);
        return response.IsSuccessStatusCode;
    }
}
