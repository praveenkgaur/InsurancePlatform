using Claims.Api.Services;
using Insurance.Contracts.Claims;
using Insurance.Contracts.Policies;
using Insurance.Helpers;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace Authentication.Bff.Api.Agent
{
    public class ClaimsTools
    {
        private readonly ClaimsService _claims;
        private readonly SqlCommandExecutor _sql;
        private readonly RecordGuardrails _guardrails;

        public ClaimsTools(ClaimsService claims, SqlCommandExecutor sql, RecordGuardrails guardrails)
        {
            _claims = claims;
            _sql = sql;
            _guardrails = guardrails;
        }

        [KernelFunction]
        [Description("Create an insurance CLAIM (a loss/file against an existing policy). Not for creating a policy or a person. Requires policyNumber, claimant, amount. Refuse if policyNumber or claimant is missing.")]
        public async Task<string> CreateClaim(
            [Description("Policy number the claim is filed against, e.g. POL-100")] string policyNumber,
            [Description("Claimant full name")] string claimantName,
            [Description("Claim amount")] decimal amount,
            [Description("Optional claimant id from the case board")] int? claimantId = null,
            [Description("Optional claim number; leave empty to auto-generate")] string claimNumber = "")
        {
            var policy = await _sql.QuerySingleAsync<InsurancePolicyDto>(
                """
                SELECT TOP 1 PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status
                FROM dbo.InsurancePolicies
                WHERE PolicyNumber = @PolicyNumber
                """,
                new { PolicyNumber = policyNumber });
            var people = await _claims.GetClaimantsAsync();
            var person = claimantId is int id
                ? people.FirstOrDefault(c => c.ClaimantId == id)
                : people.FirstOrDefault(c => c.FullName.Equals(claimantName?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));

            var guard = _guardrails.Claim(
                policyNumber,
                claimantName,
                claimantId ?? person?.ClaimantId,
                amount,
                policyExists: policy is not null,
                policyActive: string.Equals(policy?.Status, "Active", StringComparison.OrdinalIgnoreCase),
                claimantExists: person is not null);
            if (!guard.Ok)
                return JsonSerializer.Serialize(new { error = guard.Message, code = guard.Code });

            var created = await _claims.CreateAsync(new CreateClaimRequest
            {
                PolicyNumber = policyNumber,
                ClaimantName = claimantName,
                ClaimantId = person?.ClaimantId ?? claimantId,
                Amount = amount,
                ClaimNumber = claimNumber
            });
            return JsonSerializer.Serialize(created);
        }
    }
}