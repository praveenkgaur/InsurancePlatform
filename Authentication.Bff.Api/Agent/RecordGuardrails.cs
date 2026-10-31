using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Authentication.Bff.Api.Agent
{
    public sealed class GuardResult
    {
        public bool Ok { get; init; }
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
        public object? Existing { get; init; }

        public static GuardResult Pass() => new() { Ok = true };

        public static GuardResult Fail(string code, string message) =>
            new() { Ok = false, Code = code, Message = message };

        public static GuardResult Reuse(string code, string message, object existing) =>
            new() { Ok = true, Code = code, Message = message, Existing = existing };
    }

    public sealed class RecordGuardrails
    {
        private readonly IConfiguration _config;

        public RecordGuardrails(IConfiguration config) => _config = config;

        public GuardResult Claimant(string fullName, string? email, string? phone)
        {
            var name = (fullName ?? "").Trim();
            if (name.Length is < 2 or > 80)
                return GuardResult.Fail("claimant.name", "Full name must be 2–80 characters.");
            if (Regex.IsMatch(name, @"^[\d\s]+$")
                || name.Equals("test", StringComparison.OrdinalIgnoreCase)
                || name.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                return GuardResult.Fail("claimant.name", "That does not look like a person’s name.");
            if (!Regex.IsMatch(name, @"^[\p{L}][\p{L}\p{N}\s.'-]*$"))
                return GuardResult.Fail("claimant.name", "Name may contain letters, numbers, spaces, apostrophes, and hyphens only.");

            email = string.IsNullOrWhiteSpace(email) ? "" : email.Trim();
            if (_config.GetValue("Agent:Guardrails:RequireEmailOnClaimant", false) && email.Length == 0)
                return GuardResult.Fail("claimant.email", "Email is required for a new claimant.");
            if (email.Length > 0)
            {
                try { _ = new MailAddress(email); }
                catch { return GuardResult.Fail("claimant.email", "Email is not valid."); }
            }

            phone = string.IsNullOrWhiteSpace(phone) ? "" : Regex.Replace(phone, @"\s+", "");
            if (phone.Length > 0 && !Regex.IsMatch(phone, @"^\+?[0-9]{10,15}$"))
                return GuardResult.Fail("claimant.phone", "Phone must be 10–15 digits.");

            return GuardResult.Pass();
        }

        public GuardResult Policy(string policyNumber, string holderName, string productName, decimal premium)
        {
            var number = (policyNumber ?? "").Trim().ToUpperInvariant();
            var pattern = _config["Agent:Guardrails:PolicyNumberPattern"] ?? @"^[A-Za-z0-9][A-Za-z0-9_\-]{2,40}$";
            if (!Regex.IsMatch(number, pattern, RegexOptions.IgnoreCase))
                return GuardResult.Fail("policy.number", "Policy number must be 3–41 letters, digits, hyphens, or underscores (example PKTEST-POL-12 or POL-501).");

            if (string.IsNullOrWhiteSpace(holderName) || holderName.Trim().Length < 2)
                return GuardResult.Fail("policy.holder", "Holder name is required.");

            var product = (productName ?? "").Trim();
            var allowed = _config.GetSection("Agent:Guardrails:AllowedProducts").Get<string[]>()
                          ?? ["Motor", "Health", "Home", "Life", "Auto"];
            if (!allowed.Any(p => p.Equals(product, StringComparison.OrdinalIgnoreCase)))
                return GuardResult.Fail("policy.product", $"Product must be one of: {string.Join(", ", allowed)}.");

            var maxPremium = _config.GetValue("Agent:Guardrails:MaxPremium", 1_000_000m);
            if (premium <= 0 || premium > maxPremium)
                return GuardResult.Fail("policy.premium", $"Premium must be greater than 0 and at most {maxPremium}.");

            return GuardResult.Pass();
        }

        public GuardResult Claim(
            string policyNumber,
            string claimantName,
            int? claimantId,
            decimal amount,
            bool policyExists,
            bool policyActive,
            bool claimantExists)
        {
            if (string.IsNullOrWhiteSpace(policyNumber))
                return GuardResult.Fail("claim.policy", "Policy number is required.");
            if (!policyExists)
                return GuardResult.Fail("claim.policy", $"Policy {policyNumber} was not found.");
            if (!policyActive)
                return GuardResult.Fail("claim.policy", $"Policy {policyNumber} is not Active.");

            if (claimantId is null && string.IsNullOrWhiteSpace(claimantName))
                return GuardResult.Fail("claim.claimant", "Claimant id or name is required.");
            if (!claimantExists)
                return GuardResult.Fail("claim.claimant", "Claimant was not found. Create or select a claimant first.");

            var max = _config.GetValue("Agent:Guardrails:MaxClaimAmount", 1_000_000m);
            if (amount <= 0 || amount > max)
                return GuardResult.Fail("claim.amount", $"Amount must be greater than 0 and at most {max}.");

            return GuardResult.Pass();
        }

        public bool NeedsClaimConfirm(decimal amount) =>
            amount >= _config.GetValue("Agent:Guardrails:ConfirmClaimAbove", 100_000m);

        public bool ReuseExistingClaimant =>
            _config.GetValue("Agent:Guardrails:ReuseExistingClaimant", true);

        public bool ReuseExistingPolicy =>
            _config.GetValue("Agent:Guardrails:ReuseExistingPolicy", true);
    }
}
