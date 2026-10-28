using System.Net.Http.Json;
using Insurance.Contracts.Policies;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class PoliciesService
{
    private readonly HttpClient _http;

    public PoliciesService(HttpClient http) => _http = http;

    public async Task<InsurancePolicyDto[]?> GetAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/policies");
            if (!response.IsSuccessStatusCode)
                return [];
            return await response.Content.ReadFromJsonAsync<InsurancePolicyDto[]>() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<bool> CreateAsync(UpsertPolicyRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/policies", request);
        return response.IsSuccessStatusCode;
    }
}
