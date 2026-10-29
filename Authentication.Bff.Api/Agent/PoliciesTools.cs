using System.ComponentModel;
using System.Text.Json;
using Insurance.Contracts.Policies;
using Insurance.Helpers;
using Microsoft.SemanticKernel;

namespace Authentication.Bff.Api.Agent
{
    public sealed class PoliciesTools
    {
        private readonly SqlCommandExecutor _sql;

        public PoliciesTools(SqlCommandExecutor sql) => _sql = sql;

        [KernelFunction]
        [Description("List insurance POLICIES (contracts). Use when the user asks about policy numbers or holders. Not for claims.")]
        public async Task<string> GetPolicies()
        {
            var rows = await _sql.QueryAsync<InsurancePolicyDto>("""
                SELECT PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status
                FROM dbo.InsurancePolicies
                ORDER BY PolicyId DESC
                """);
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
            await _sql.ExecuteAsync(
                """
                INSERT INTO dbo.InsurancePolicies (PolicyNumber, HolderName, ProductName, Premium, Status)
                VALUES (@PolicyNumber, @HolderName, @ProductName, @Premium, @Status)
                """,
                new
                {
                    PolicyNumber = policyNumber,
                    HolderName = holderName,
                    ProductName = productName,
                    Premium = premium,
                    Status = "Active"
                });
            var created = await _sql.QuerySingleAsync<InsurancePolicyDto>(
                "SELECT TOP 1 PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status FROM dbo.InsurancePolicies WHERE PolicyNumber = @PolicyNumber",
                new { PolicyNumber = policyNumber });
            return JsonSerializer.Serialize(created);
        }
    }
}
