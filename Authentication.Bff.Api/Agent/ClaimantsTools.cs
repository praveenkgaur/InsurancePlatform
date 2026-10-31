using Claims.Api.Services;
using Insurance.Contracts.Claims;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace Authentication.Bff.Api.Agent
{
    public sealed class ClaimantsTools
    {
        private readonly ClaimsService _claims;
        private readonly RecordGuardrails _guardrails;

        public ClaimantsTools(ClaimsService claims, RecordGuardrails guardrails)
        {
            _claims = claims;
            _guardrails = guardrails;
        }

        [KernelFunction]
        [Description("List insurance claimants (people). Use when the user asks who the claimants are.")]
        public async Task<string> GetClaimants()
        {
            var rows = await _claims.GetClaimantsAsync();
            return JsonSerializer.Serialize(rows);
        }

        [KernelFunction]
        [Description("Create a claimant (a person). Not a policy and not a claim. Requires fullName.")]
        public async Task<string> CreateClaimant(
            [Description("Full name of the person")] string fullName,
            [Description("Optional email")] string email = "",
            [Description("Optional phone")] string phone = "")
        {
            var guard = _guardrails.Claimant(fullName, email, phone);
            if (!guard.Ok)
                return JsonSerializer.Serialize(new { error = guard.Message, code = guard.Code });

            if (_guardrails.ReuseExistingClaimant)
            {
                var existing = await _claims.GetClaimantsAsync();
                var match = existing.FirstOrDefault(c =>
                    (!string.IsNullOrWhiteSpace(email) && c.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase))
                    || c.FullName.Equals(fullName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return JsonSerializer.Serialize(new { reused = true, match.ClaimantId, match.FullName, match.Email, match.Phone });
            }

            var created = await _claims.CreateClaimantAsync(new CreateClaimantRequest
            {
                FullName = fullName.Trim(),
                Email = email ?? "",
                Phone = phone ?? ""
            });
            return JsonSerializer.Serialize(created);
        }
    }
}