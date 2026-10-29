using Insurance.Contracts.Claims;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Authentication.Bff.Api.Agent
{
    public class ClaimsTools
    {
        private readonly HttpClient _http;
        public ClaimsTools(HttpClient http, string accessToken)
        {
            _http = http;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        [KernelFunction]
        [Description("List insurance claimants (people). Use when the user asks who the claimants are.")]
        public async Task<string> GetClaimants()
        {
            var rows = await _http.GetFromJsonAsync<InsuranceClaimantDto[]>("api/claimants") ?? [];
            return JsonSerializer.Serialize(rows);
        }

        [KernelFunction]
        [Description("Create an insurance CLAIM (a loss/file against an existing policy). Not for creating a policy contract. Requires policyNumber, claimantName, amount.")]
        public async Task<string> CreateClaim(
        [Description("Policy number the claim is filed against, e.g. POL-100")] string policyNumber,
        [Description("Claimant full name")] string claimantName,
        [Description("Claim amount")] decimal amount,
        [Description("Optional claim number; leave empty to auto-generate")] string claimNumber = "")
        {
            var response = await _http.PostAsJsonAsync("api/claims", new CreateClaimRequest
            {
                PolicyNumber = policyNumber,
                ClaimantName = claimantName,
                Amount = amount,
                ClaimNumber = claimNumber
            });
            var body = await response.Content.ReadAsStringAsync();
            return $"HTTP {(int)response.StatusCode} {body}";
        }


    }
}
