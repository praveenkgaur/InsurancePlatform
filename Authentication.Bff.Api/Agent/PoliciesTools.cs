using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Insurance.Contracts.Policies;
using Microsoft.SemanticKernel;

namespace Authentication.Bff.Api.Agent
{
    public sealed class PoliciesTools
    {
        private readonly HttpClient _http;

        public PoliciesTools(HttpClient http, string accessToken)
        {
            _http = http;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        [KernelFunction]
        [Description("List insurance POLICIES (contracts). Use when the user asks about policy numbers or holders. Not for claims.")]
        public async Task<string> GetPolicies()
        {
            var rows = await _http.GetFromJsonAsync<InsurancePolicyDto[]>("api/policies") ?? [];
            return JsonSerializer.Serialize(rows);
        }

        [KernelFunction]
        [Description("Create an insurance POLICY (a contract for a holder). Not for filing a claim/loss.")]
        public async Task<string> CreatePolicy(
            [Description("Policy number")] string policyNumber,
            [Description("Policy holder name")] string holderName,
            [Description("Product name e.g. Auto")] string productName,
            [Description("Premium amount")] decimal premium)
        {
            var response = await _http.PostAsJsonAsync("api/policies", new UpsertPolicyRequest
            {
                PolicyNumber = policyNumber,
                HolderName = holderName,
                ProductName = productName,
                Premium = premium,
                Status = "Active"
            });
            var body = await response.Content.ReadAsStringAsync();
            return $"HTTP {(int)response.StatusCode} {body}";
        }
    }
}

