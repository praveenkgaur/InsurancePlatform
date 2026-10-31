using System.Net.Mail;
using System.Text.RegularExpressions;
using Insurance.Contracts.Agent;

namespace Authentication.Bff.Api.Agent
{
    public sealed class GuidedIntakeResult
    {
        public CaseBoardDto Board { get; init; } = new();
        public AgentIntakeDto Intake { get; init; } = new();
        public bool ReadyToRun { get; init; }
    }

    /// <summary>
    /// Guided intake from Mdfiles/Assistant.GuidedIntake.Design.md:
    /// CollectIntake asks claimant → policy → claim, then ReadyToRun starts specialists.
    /// </summary>
    public static class GuidedIntake
    {
        public const string StepClaimant = "Claimant";
        public const string StepPolicy = "Policy";
        public const string StepClaim = "Claim";
        public const string StepRun = "Run";
        public const string StepDone = "Done";

        public static AgentIntakeDto Start() => ForStep(StepClaimant);

        public static AgentIntakeDto ForStep(string step, string? prompt = null) =>
            step switch
            {
                StepPolicy => new AgentIntakeDto
                {
                    Step = StepPolicy,
                    Prompt = prompt ?? "Claimant is on the board. What is the policy number? Any value, e.g. POL-2001 or SKPK_POL_1",
                    Watermark = "Policy number (any value)",
                    Help = "Step 2 of 3 — holder defaults to the claimant on the board. Optional: product, premium."
                },
                StepClaim => new AgentIntakeDto
                {
                    Step = StepClaim,
                    Prompt = prompt ?? "Policy is on the board. Claim number and amount? Any claim number, then amount 5000",
                    Watermark = "Claim number and amount (any number)",
                    Help = "Step 3 of 3 — then Create runs."
                },
                StepRun => new AgentIntakeDto
                {
                    Step = StepRun,
                    Prompt = prompt ?? "Assigning Claimant agent → Policy agent → Claim agent.",
                    Watermark = "Agents are creating claimant, policy, claim…",
                    Help = "Specialists are creating the records from the case board."
                },
                StepDone => new AgentIntakeDto
                {
                    Step = StepDone,
                    Prompt = prompt ?? "",
                    Watermark = "Ask another case, or type claimant / policy / claim numbers",
                    Help = "Board shows the three numbers. You can start another case."
                },
                _ => new AgentIntakeDto
                {
                    Step = StepClaimant,
                    Prompt = prompt ?? "What is the claimant full name? Any name, e.g. claimant_1011 or Priya",
                    Watermark = "Claimant full name (any value)",
                    Help = "Step 1 of 3 — person first. Optional: email, phone."
                }
            };

        public static GuidedIntakeResult CollectIntake(
            CaseBoardDto board,
            string message,
            RecordGuardrails guardrails,
            bool requireAmount)
        {
            ApplyGuidedIntakeMessage(board, message);

            if (string.IsNullOrWhiteSpace(board.Claimant.FullName))
            {
                return Incomplete(board, StepClaimant, "What is the claimant full name? Any name is fine.");
            }

            var nameGuard = guardrails.Claimant(board.Claimant.FullName, board.Claimant.Email, board.Claimant.Phone);
            if (!nameGuard.Ok)
            {
                board.Claimant.FullName = "";
                return Incomplete(board, StepClaimant, nameGuard.Message);
            }

            board.Claimant.Status = string.IsNullOrWhiteSpace(board.Claimant.Status) || board.Claimant.Status == "empty"
                ? "working"
                : board.Claimant.Status;

            if (string.IsNullOrWhiteSpace(board.Policy.PolicyNumber))
            {
                return Incomplete(
                    board,
                    StepPolicy,
                    $"Claimant {board.Claimant.FullName} is on the board. What is the policy number? Any value is fine.");
            }

            var holder = string.IsNullOrWhiteSpace(board.Claimant.FullName) ? "Pending" : board.Claimant.FullName;
            var product = string.IsNullOrWhiteSpace(board.Policy.Product) ? "Motor" : board.Policy.Product;
            var premium = board.Policy.Premium is > 0 ? board.Policy.Premium.Value : 8000m;
            var policyGuard = guardrails.Policy(board.Policy.PolicyNumber, holder, product, premium);
            if (!policyGuard.Ok)
            {
                board.Policy.PolicyNumber = "";
                return Incomplete(board, StepPolicy, policyGuard.Message);
            }

            board.Policy.HolderName = holder;
            board.Policy.Product = product;
            board.Policy.Premium = premium;
            board.Policy.Status = string.IsNullOrWhiteSpace(board.Policy.Status) || board.Policy.Status == "empty"
                ? "working"
                : board.Policy.Status;

            if (string.IsNullOrWhiteSpace(board.Claim.ClaimNumber) || (requireAmount && board.Claim.Amount is null or <= 0))
            {
                return Incomplete(
                    board,
                    StepClaim,
                    $"Policy {board.Policy.PolicyNumber} noted (holder {holder}). Claim number and amount? Any claim number, then amount 5000");
            }

            return new GuidedIntakeResult
            {
                Board = board,
                ReadyToRun = true,
                Intake = ForStep(StepRun, "All fields present. Assigning Claimant agent → Policy agent → Claim agent.")
            };
        }

        public static bool IsReadyToRun(GuidedIntakeResult result) => result.ReadyToRun;

        private static GuidedIntakeResult Incomplete(CaseBoardDto board, string step, string prompt) =>
            new()
            {
                Board = board,
                ReadyToRun = false,
                Intake = ForStep(step, prompt)
            };

        public static void ApplyGuidedIntakeMessage(CaseBoardDto board, string message)
        {
            var text = (message ?? "").Trim();
            if (text.Length == 0)
                return;

            var email = ExtractEmail(text);
            if (!string.IsNullOrWhiteSpace(email))
                board.Claimant.Email = email;
            var phone = ExtractPhone(text);
            if (!string.IsNullOrWhiteSpace(phone))
                board.Claimant.Phone = phone;

            var amount = ExtractAmount(text);
            if (amount is not null)
                board.Claim.Amount = amount;

            var named = ExtractClaimantName(text);
            var policy = ExtractPolicyNumber(text);
            var claimNumber = ExtractClaimNumber(text);
            if (!string.IsNullOrWhiteSpace(named))
                SetClaimantName(board, named);
            if (!string.IsNullOrWhiteSpace(policy))
                SetPolicyNumber(board, policy);
            if (!string.IsNullOrWhiteSpace(claimNumber))
                SetClaimNumber(board, claimNumber);

            var field = FirstField(text);
            if (string.IsNullOrWhiteSpace(field) || field.Contains('@'))
                return;

            if (string.IsNullOrWhiteSpace(board.Claimant.FullName))
            {
                SetClaimantName(board, field);
                return;
            }

            if (string.IsNullOrWhiteSpace(board.Policy.PolicyNumber)
                && string.IsNullOrWhiteSpace(policy)
                && !LooksLikeClaimantToken(field))
            {
                SetPolicyNumber(board, field);
                return;
            }

            if (string.IsNullOrWhiteSpace(board.Claim.ClaimNumber)
                && string.IsNullOrWhiteSpace(claimNumber)
                && !LooksLikeClaimantToken(field)
                && !LooksLikePolicyNumber(field))
            {
                SetClaimNumber(board, field);
            }
        }

        private static void SetClaimantName(CaseBoardDto board, string name)
        {
            name = name.Trim();
            if (!name.Equals(board.Claimant.FullName, StringComparison.OrdinalIgnoreCase))
            {
                board.Claimant.Id = null;
                board.Claimant.Email = board.Claimant.Email;
                board.Claimant.Phone = "";
            }
            board.Claimant.FullName = name;
            board.Claimant.Status = "working";
        }

        private static void SetPolicyNumber(CaseBoardDto board, string number)
        {
            number = number.Trim();
            if (!number.Equals(board.Policy.PolicyNumber, StringComparison.OrdinalIgnoreCase))
                board.Policy.Id = null;
            board.Policy.PolicyNumber = number;
            board.Policy.Status = "working";
        }

        private static void SetClaimNumber(CaseBoardDto board, string number)
        {
            number = number.Trim();
            if (!number.Equals(board.Claim.ClaimNumber, StringComparison.OrdinalIgnoreCase))
                board.Claim.Id = null;
            board.Claim.ClaimNumber = number;
            board.Claim.Status = "working";
        }

        public static string? ExtractPolicyNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (Regex.IsMatch(text.Trim(), @"^[A-Za-z0-9][A-Za-z0-9_\-]{1,40}$") && LooksLikePolicyNumber(text))
                return text.Trim();
            var labeled = Regex.Match(text, @"policy(?:\s*number)?\s*(?:[:\-]+|\bis\b)\s*([A-Za-z0-9][A-Za-z0-9_\-]*)", RegexOptions.IgnoreCase);
            if (labeled.Success)
                return labeled.Groups[1].Value.Trim();
            var tagged = Regex.Match(text, @"\b([A-Za-z0-9]+[_-]POL[_-][A-Za-z0-9_\-]+)\b", RegexOptions.IgnoreCase);
            if (tagged.Success)
                return tagged.Groups[1].Value.Trim();
            var classic = Regex.Match(text, @"\b(POL-\d{3,10})\b", RegexOptions.IgnoreCase);
            return classic.Success ? classic.Groups[1].Value.ToUpperInvariant() : null;
        }

        public static string ExtractClaimantName(string text)
        {
            var m = Regex.Match(text, @"claimant(?:\s+name)?\s*(?:[:\-]+|\bis\b)\s*([^,;\n]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public static string ExtractClaimNumber(string text)
        {
            var m = Regex.Match(text, @"claim\s*number\s*(?:[:\-]+|\bis\b)\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var value = m.Groups[1].Value.Trim();
                return LooksLikeClaimantToken(value) ? "" : value;
            }
            var token = FirstField(text);
            return LooksLikeClaimNumber(token) ? token : "";
        }

        public static decimal? ExtractAmount(string text)
        {
            var m = Regex.Match(text, @"amount\s*[:\-]?\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
            if (m.Success && decimal.TryParse(m.Groups[1].Value, out var amount))
                return amount;
            return null;
        }

        private static string ExtractEmail(string text)
        {
            var m = Regex.Match(text, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            if (!m.Success)
                return "";
            try
            {
                _ = new MailAddress(m.Value);
                return m.Value;
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractPhone(string text)
        {
            var m = Regex.Match(text ?? "", @"\+?[0-9]{10,15}");
            return m.Success ? m.Value : "";
        }

        private static bool LooksLikePolicyNumber(string text) =>
            Regex.IsMatch((text ?? "").Trim(), @"POL", RegexOptions.IgnoreCase);

        private static bool LooksLikeClaimNumber(string text)
        {
            var t = (text ?? "").Trim();
            if (LooksLikeClaimantToken(t))
                return false;
            return Regex.IsMatch(t, @"(_|-|^)(CLAIM|CLM)(_|-|[0-9])", RegexOptions.IgnoreCase)
                || Regex.IsMatch(t, @"^(CLAIM|CLM)[_-][A-Za-z0-9_\-]+$", RegexOptions.IgnoreCase);
        }

        private static bool LooksLikeClaimantToken(string text) =>
            Regex.IsMatch((text ?? "").Trim(), @"^claimant($|[^A-Za-z])", RegexOptions.IgnoreCase);

        private static string FirstField(string text)
        {
            var part = (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault(s => s.Length > 0 && !s.Contains('@') && !Regex.IsMatch(s, @"^\+?[0-9]{10,15}$"))
                ?? "";
            if (part.Contains(' ') && !LooksLikePolicyNumber(part) && !LooksLikeClaimNumber(part))
                return part;
            return part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? part;
        }
    }
}
