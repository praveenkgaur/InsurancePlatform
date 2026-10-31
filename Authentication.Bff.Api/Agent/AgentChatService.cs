using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Claims.Api.Services;
using Insurance.Contracts.Agent;
using Insurance.Helpers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Authentication.Bff.Api.Agent
{
    public sealed class AgentChatService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;
        private readonly AgentToolLogStore _toolLog;
        private readonly ClaimsService _claims;
        private readonly SqlCommandExecutor _sql;
        private readonly RecordGuardrails _guardrails;

        public AgentChatService(
            IConfiguration config,
            IHttpClientFactory http,
            AgentToolLogStore toolLog,
            ClaimsService claims,
            SqlCommandExecutor sql,
            RecordGuardrails guardrails)
        {
            _config = config;
            _http = http;
            _toolLog = toolLog;
            _claims = claims;
            _sql = sql;
            _guardrails = guardrails;
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                var message = request.Message ?? "";
                if (_config.GetValue("Agent:UsePythonAgents", false))
                    return await ForwardPythonAsync(message, accessToken, cancellationToken);

                var missing = MissingLlmSettings();
                if (missing is not null)
                    return new AgentChatResponse { Reply = missing };

                var mode = (_config["Agent:Mode"] ?? "Single").Trim();
                if (mode.Equals("Multi", StringComparison.OrdinalIgnoreCase))
                    return await RunMultiAsync(request, cancellationToken);

                return await RunSingleAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                return new AgentChatResponse
                {
                    Reply = "Assistant could not call the LLM. " + ex.Message
                };
            }
        }

        private string? MissingLlmSettings()
        {
            var provider = (_config["Agent:Llm:Provider"] ?? "OpenAI").Trim();
            var apiKey = _config["Agent:Llm:ApiKey"]
                ?? _config["Agent:OpenAI:ApiKey"]
                ?? _config["Agent:AzureOpenAI:ApiKey"]
                ?? "";
            if (!string.IsNullOrWhiteSpace(apiKey))
                return null;
            return "LLM API key is empty. Set Agent:Llm:ApiKey in appsettings.Development.local.json and restart the BFF (not only the UI).";
        }

        private async Task<AgentChatResponse> RunSingleAsync(AgentChatRequest request, CancellationToken cancellationToken)
        {
            var kernel = BuildKernel();
            kernel.ImportPluginFromObject(new ClaimantsTools(_claims, _guardrails), "claimants");
            kernel.ImportPluginFromObject(new ClaimsTools(_claims, _sql, _guardrails), "claims");
            kernel.ImportPluginFromObject(new PoliciesTools(_sql, _guardrails), "policies");

            var toolsCalled = new List<string>();
            kernel.FunctionInvocationFilters.Add(new ToolNameFilter(toolsCalled, _toolLog, "Single"));

            var history = NewHistory(
                "You are the Insurance Platform assistant. " +
                "A CLAIM is a loss filed against a policy. A POLICY is a contract. A CLAIMANT is a person. " +
                "When the user wants a person, call claimants.CreateClaimant. " +
                "When they want a contract, call policies.CreatePolicy. " +
                "When they want to file a loss and you have policyNumber, claimant, and amount, call claims.CreateClaim. " +
                "Do not ask for confirmation. Never invent URLs.");
            FillHistory(history, request);

            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var settings = new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };
            var result = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
            return new AgentChatResponse
            {
                Mode = "Single",
                Reply = result.Content ?? "",
                ToolsCalled = toolsCalled.ToArray()
            };
        }

        private async Task<AgentChatResponse> RunMultiAsync(AgentChatRequest request, CancellationToken cancellationToken)
        {
            var board = CloneBoard(request.CaseBoard);
            var steps = new List<AgentStepDto>();
            var allTools = new List<string>();
            var intakeEnabled = _config.GetValue("Agent:Intake:Enabled", true);
            AgentIntakeDto intake = GuidedIntake.Start();
            if (intakeEnabled)
            {
                var collected = GuidedIntake.CollectIntake(
                    board,
                    request.Message ?? "",
                    _guardrails,
                    _config.GetValue("Agent:Intake:RequireAmount", true));
                board = collected.Board;
                intake = collected.Intake;
                if (!GuidedIntake.IsReadyToRun(collected))
                    return FinishGuidedIntake(collected);
            }
            else
            {
                FillBoardFromUtterance(board, request.Message ?? "");
            }

            var text = (request.Message ?? "") + " " + string.Join(" ", (request.History ?? []).Select(h => h.Text));
            var wantClaimant = intakeEnabled
                || ContainsAny(text, "claimant", "onboard", "person", "customer")
                || !string.IsNullOrWhiteSpace(board.Claimant.FullName);
            var wantPolicy = intakeEnabled
                || ContainsAny(text, "policy", "onboard", "motor", "premium", "contract")
                || !string.IsNullOrWhiteSpace(board.Policy.PolicyNumber);
            var wantClaim = intakeEnabled
                || ContainsAny(text, "claim", "file", "loss", "onboard")
                || !string.IsNullOrWhiteSpace(ExtractClaimNumber(request.Message ?? ""));

            steps.Add(new AgentStepDto
            {
                AgentKey = "Coordinator",
                DisplayName = _config["Agent:Orchestrator"] ?? "Coordinator",
                Status = "done",
                Message = "Assigned work: Claimant agent, then Policy agent, then Claim agent. They run one after another, not in parallel.",
                Utc = DateTime.UtcNow
            });
            board.Handoffs.Add(new HandoffDto
            {
                From = "Coordinator",
                To = "Claimant",
                Reason = "Intake complete. Start Claimant agent first.",
                Utc = DateTime.UtcNow
            });

            // Coordinator: only run what the utterance needs. Claim still requires claimant+policy on the board.
            if (AgentEnabled("Claimant") && (wantClaimant || wantClaim || wantPolicy))
            {
                var step = await RunSpecialistAsync(
                    "Claimant",
                    _config["Agent:Agents:Claimant:DisplayName"] ?? "Claimant agent",
                    "You are the Claimant agent. Find or create the person only. Use claimants tools. Write id and name. Do not create policies or claims.",
                    k => k.ImportPluginFromObject(new ClaimantsTools(_claims, _guardrails), "claimants"),
                    request,
                    "Claimant",
                    cancellationToken);
                steps.Add(step);
                MergeTools(allTools, step);
                ApplyClaimantToBoard(board, step);
                await EnsureClaimantOnBoardAsync(board, request.Message ?? "");
                if (step.Status == "needs_input" && board.Claimant.Id is null)
                    return FinishMulti("Need one more claimant field.", board, steps, allTools);
            }
            else
            {
                steps.Add(Skip("Claimant", _config["Agent:Agents:Claimant:DisplayName"] ?? "Claimant agent", "Not requested."));
                board.Claimant.Status = "skipped";
            }

            if (AgentEnabled("Policy") && wantPolicy)
            {
                if (board.Claimant.Id is not null || !string.IsNullOrWhiteSpace(board.Claimant.FullName))
                    board.Handoffs.Add(new HandoffDto
                    {
                        From = "Claimant",
                        To = "Policy",
                        Reason = $"{board.Claimant.FullName} is claimant #{board.Claimant.Id}",
                        Utc = DateTime.UtcNow
                    });

                var step = await RunSpecialistAsync(
                    "Policy",
                    _config["Agent:Agents:Policy:DisplayName"] ?? "Policy agent",
                    "You are the Policy agent. Find or create the contract only. Use the EXACT policy number from the user message. Never invent a different POL- number. If that number is missing in the database, call CreatePolicy with that number. Holder can be the claimant name. Do not create claimants or claims.",
                    k => k.ImportPluginFromObject(new PoliciesTools(_sql, _guardrails), "policies"),
                    request,
                    "Policy",
                    cancellationToken);
                steps.Add(step);
                MergeTools(allTools, step);
                ApplyPolicyToBoard(board, step);
                await EnsurePolicyOnBoardAsync(board);
                if (step.Status == "needs_input" && string.IsNullOrWhiteSpace(board.Policy.PolicyNumber))
                    return FinishMulti("Need one more policy field.", board, steps, allTools);
            }
            else
            {
                steps.Add(Skip("Policy", _config["Agent:Agents:Policy:DisplayName"] ?? "Policy agent", wantPolicy ? "Disabled." : "Not requested."));
                board.Policy.Status = "skipped";
            }

            if (AgentEnabled("Claim") && wantClaim)
            {
                await EnsureClaimantOnBoardAsync(board, request.Message ?? "");
                await EnsurePolicyOnBoardAsync(board);
                var policyOk = !string.IsNullOrWhiteSpace(board.Policy.PolicyNumber);
                var personOk = board.Claimant.Id is not null || !string.IsNullOrWhiteSpace(board.Claimant.FullName);
                if (!policyOk || !personOk)
                {
                    board.Missing.Clear();
                    if (!personOk) board.Missing.Add("claimant");
                    if (!policyOk) board.Missing.Add("policy.number");
                    steps.Add(new AgentStepDto
                    {
                        AgentKey = "Claim",
                        DisplayName = _config["Agent:Agents:Claim:DisplayName"] ?? "Claim agent",
                        Status = "needs_input",
                        Message = "CreateClaim needs a claimant and a policy number first.",
                        Utc = DateTime.UtcNow
                    });
                    return FinishMulti("I still need a claimant and a policy number before filing the claim.", board, steps, allTools);
                }

                var amount = board.Claim.Amount ?? ExtractAmount(request.Message ?? "");
                if (amount is null or <= 0)
                {
                    if (intakeEnabled)
                    {
                        intake = GuidedIntake.ForStep(
                            GuidedIntake.StepClaim,
                            $"Policy {board.Policy.PolicyNumber} is ready. Claim number and amount? Example: SKPK_CLAIM_01 amount 5000");
                        return FinishGuidedIntake(new GuidedIntakeResult { Board = board, Intake = intake, ReadyToRun = false });
                    }
                    amount = 1000m;
                }
                var claimNumber = ExtractClaimNumber(request.Message ?? "");
                if (string.IsNullOrWhiteSpace(claimNumber))
                    claimNumber = board.Claim.ClaimNumber ?? "";

                board.Handoffs.Add(new HandoffDto
                {
                    From = "Policy",
                    To = "Claim",
                    Reason = $"File against {board.Policy.PolicyNumber} for {board.Claimant.FullName} #{board.Claimant.Id}",
                    Utc = DateTime.UtcNow
                });

                var extra = $" Case board: claimantId={board.Claimant.Id}, claimantName={board.Claimant.FullName}, policyNumber={board.Policy.PolicyNumber}, amount={amount}, claimNumber={claimNumber}.";
                var step = await RunSpecialistAsync(
                    "Claim",
                    _config["Agent:Agents:Claim:DisplayName"] ?? "Claim agent",
                    "You are the Claim agent. File a claim only. You MUST call claims.CreateClaim now using the case board values. Do not ask for confirmation." + extra,
                    k =>
                    {
                        k.ImportPluginFromObject(new ClaimsTools(_claims, _sql, _guardrails), "claims");
                    },
                    request,
                    "Claim",
                    cancellationToken);
                steps.Add(step);
                MergeTools(allTools, step);
                ApplyClaimToBoard(board, step);
                var createdNumber = board.Claim.ClaimNumber ?? "";
                if (string.IsNullOrWhiteSpace(createdNumber)
                    || (!string.IsNullOrWhiteSpace(claimNumber)
                        && !createdNumber.Equals(claimNumber, StringComparison.OrdinalIgnoreCase)))
                    await FileClaimFromBoardAsync(board, amount.Value, claimNumber, steps);
            }
            else
            {
                steps.Add(Skip("Claim", _config["Agent:Agents:Claim:DisplayName"] ?? "Claim agent", wantClaim ? "Disabled." : "Not requested."));
                board.Claim.Status = "skipped";
            }

            var reply = BuildSummary(board, steps);
            return FinishMulti(reply, board, steps, allTools, GuidedIntake.ForStep(GuidedIntake.StepDone, reply));
        }

        private async Task<AgentStepDto> RunSpecialistAsync(
            string key,
            string displayName,
            string system,
            Action<Kernel> import,
            AgentChatRequest request,
            string agentName,
            CancellationToken cancellationToken)
        {
            var kernel = BuildKernel();
            import(kernel);
            var toolsCalled = new List<string>();
            var toolJson = new List<string>();
            kernel.FunctionInvocationFilters.Add(new ToolNameFilter(toolsCalled, _toolLog, agentName, toolJson));
            var history = NewHistory(system);
            FillHistory(history, request);
            try
            {
                var chat = kernel.GetRequiredService<IChatCompletionService>();
                var settings = new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };
                var result = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
                var status = toolsCalled.Count == 0 && LooksLikeQuestion(result.Content) ? "needs_input" : "done";
                return new AgentStepDto
                {
                    AgentKey = key,
                    DisplayName = displayName,
                    Status = status,
                    Message = result.Content ?? "",
                    Tools = toolsCalled.ToArray(),
                    ToolResultJson = string.Join("\n", toolJson),
                    Utc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new AgentStepDto
                {
                    AgentKey = key,
                    DisplayName = displayName,
                    Status = "failed",
                    Message = ex.Message,
                    Tools = toolsCalled.ToArray(),
                    ToolResultJson = string.Join("\n", toolJson),
                    Utc = DateTime.UtcNow
                };
            }
        }

        private Kernel BuildKernel()
        {
            var builder = Kernel.CreateBuilder();
            AddChatModel(builder);
            return builder.Build();
        }

        private bool AgentEnabled(string key) =>
            _config.GetValue($"Agent:Agents:{key}:Enabled", true);

        private static AgentStepDto Skip(string key, string display, string reason) =>
            new()
            {
                AgentKey = key,
                DisplayName = display,
                Status = "skipped",
                Message = reason,
                Utc = DateTime.UtcNow
            };

        private static AgentChatResponse FinishMulti(
            string reply,
            CaseBoardDto board,
            List<AgentStepDto> steps,
            List<string> tools,
            AgentIntakeDto? intake = null) =>
            new()
            {
                Mode = "Multi",
                Reply = reply,
                ToolsCalled = tools.ToArray(),
                CaseBoard = board,
                Steps = steps,
                Intake = intake ?? GuidedIntake.ForStep(GuidedIntake.StepDone, reply)
            };

        private static AgentChatResponse FinishGuidedIntake(GuidedIntakeResult collected) =>
            new()
            {
                Mode = "Multi",
                Reply = collected.Intake.Prompt,
                CaseBoard = collected.Board,
                Intake = collected.Intake,
                Steps =
                [
                    new AgentStepDto
                    {
                        AgentKey = "Coordinator",
                        DisplayName = "Coordinator",
                        Status = "needs_input",
                        Message = collected.Intake.Prompt,
                        Utc = DateTime.UtcNow
                    }
                ]
            };

        private static void MergeTools(List<string> all, AgentStepDto step)
        {
            foreach (var t in step.Tools)
                all.Add(t);
        }

        private static ChatHistory NewHistory(string system) => new(system);

        private static void FillHistory(ChatHistory history, AgentChatRequest request)
        {
            foreach (var turn in request.History ?? [])
            {
                if (string.IsNullOrWhiteSpace(turn.Text))
                    continue;
                if (turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    history.AddAssistantMessage(turn.Text);
                else
                    history.AddUserMessage(turn.Text);
            }
            history.AddUserMessage(request.Message ?? "");
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
            {
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool LooksLikeQuestion(string? content) =>
            !string.IsNullOrWhiteSpace(content) && content.Contains('?');

        private static CaseBoardDto CloneBoard(CaseBoardDto? source)
        {
            if (source is null)
                return new CaseBoardDto();
            return new CaseBoardDto
            {
                Claimant = new CasePersonDto
                {
                    Id = source.Claimant.Id,
                    FullName = source.Claimant.FullName,
                    Email = source.Claimant.Email,
                    Phone = source.Claimant.Phone,
                    Status = source.Claimant.Status
                },
                Policy = new CasePolicyDto
                {
                    Id = source.Policy.Id,
                    PolicyNumber = source.Policy.PolicyNumber,
                    HolderName = source.Policy.HolderName,
                    Product = source.Policy.Product,
                    Premium = source.Policy.Premium,
                    Status = source.Policy.Status
                },
                Claim = new CaseClaimDto
                {
                    Id = source.Claim.Id,
                    ClaimNumber = source.Claim.ClaimNumber,
                    Amount = source.Claim.Amount,
                    Status = source.Claim.Status
                }
            };
        }

        private static void FillBoardFromUtterance(CaseBoardDto board, string message)
        {
            var name = ExtractClaimantName(message);
            if (!string.IsNullOrWhiteSpace(name)
                && !name.Equals(board.Claimant.FullName, StringComparison.OrdinalIgnoreCase))
            {
                board.Claimant = new CasePersonDto { FullName = name, Status = "working" };
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                board.Claimant.FullName = name;
            }

            var policy = ExtractPolicyNumber(message);
            if (!string.IsNullOrWhiteSpace(policy)
                && !policy.Equals(board.Policy.PolicyNumber, StringComparison.OrdinalIgnoreCase))
            {
                board.Policy = new CasePolicyDto { PolicyNumber = policy, Status = "working" };
            }
            else if (!string.IsNullOrWhiteSpace(policy))
            {
                board.Policy.PolicyNumber = policy;
            }

            var amount = ExtractAmount(message);
            if (amount is not null)
                board.Claim.Amount = amount;
            var claimNumber = ExtractClaimNumber(message);
            if (!string.IsNullOrWhiteSpace(claimNumber)
                && !claimNumber.Equals(board.Claim.ClaimNumber, StringComparison.OrdinalIgnoreCase))
            {
                board.Claim.ClaimNumber = claimNumber;
                board.Claim.Id = null;
                board.Claim.Status = "working";
            }
        }

        private async Task EnsureClaimantOnBoardAsync(CaseBoardDto board, string message)
        {
            var name = ExtractClaimantName(message);
            if (string.IsNullOrWhiteSpace(name))
                name = board.Claimant.FullName;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (board.Claimant.Id is not null)
                    board.Claimant.Status = "created";
                return;
            }

            if (!string.IsNullOrWhiteSpace(board.Claimant.FullName)
                && !board.Claimant.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                board.Claimant.Id = null;
                board.Claimant.Email = "";
                board.Claimant.Phone = "";
            }

            board.Claimant.FullName = name;
            var people = await _claims.GetClaimantsAsync();
            var match = people.FirstOrDefault(c => c.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null && !string.IsNullOrWhiteSpace(name))
            {
                var json = await new ClaimantsTools(_claims, _guardrails).CreateClaimant(name);
                ApplyJsonObjectToClaimant(board, json);
            }
            else if (match is not null)
            {
                board.Claimant.Id = match.ClaimantId;
                board.Claimant.FullName = match.FullName;
                board.Claimant.Email = match.Email;
                board.Claimant.Phone = match.Phone;
                board.Claimant.Status = "created";
            }
        }

        private async Task EnsurePolicyOnBoardAsync(CaseBoardDto board)
        {
            var number = board.Policy.PolicyNumber;
            if (string.IsNullOrWhiteSpace(number))
                return;
            var existing = await _sql.QuerySingleAsync<Insurance.Contracts.Policies.InsurancePolicyDto>(
                """
                SELECT TOP 1 PolicyId, PolicyNumber, HolderName, ProductName, Premium, Status
                FROM dbo.InsurancePolicies
                WHERE PolicyNumber = @PolicyNumber
                """,
                new { PolicyNumber = number });
            if (existing is null)
            {
                var holder = string.IsNullOrWhiteSpace(board.Claimant.FullName) ? "Unknown" : board.Claimant.FullName;
                var json = await new PoliciesTools(_sql, _guardrails).CreatePolicy(number, holder, "Motor", 8000);
                ApplyJsonObjectToPolicy(board, json);
                return;
            }

            board.Policy.Id = existing.PolicyId;
            board.Policy.PolicyNumber = existing.PolicyNumber;
            board.Policy.HolderName = existing.HolderName;
            board.Policy.Product = existing.ProductName;
            board.Policy.Premium = existing.Premium;
            board.Policy.Status = "created";
        }

        private async Task FileClaimFromBoardAsync(CaseBoardDto board, decimal amount, string claimNumber, List<AgentStepDto> steps)
        {
            var json = await new ClaimsTools(_claims, _sql, _guardrails).CreateClaim(
                board.Policy.PolicyNumber,
                board.Claimant.FullName,
                amount,
                board.Claimant.Id,
                claimNumber);
            ApplyJsonObjectToClaim(board, json);
            steps.Add(new AgentStepDto
            {
                AgentKey = "Claim",
                DisplayName = _config["Agent:Agents:Claim:DisplayName"] ?? "Claim agent",
                Status = string.IsNullOrWhiteSpace(board.Claim.ClaimNumber) ? "failed" : "done",
                Message = json,
                Tools = ["claims.CreateClaim"],
                ToolResultJson = json,
                Utc = DateTime.UtcNow
            });
        }

        private static void ApplyClaimantToBoard(CaseBoardDto board, AgentStepDto step)
        {
            ApplyJsonObjectToClaimant(board, step.ToolResultJson);
            ApplyJsonObjectToClaimant(board, step.Message);
            if (board.Claimant.Id is not null)
                board.Claimant.Status = "created";
            else if (step.Status == "done")
                board.Claimant.Status = string.IsNullOrWhiteSpace(board.Claimant.FullName) ? "working" : board.Claimant.Status;
        }

        private static void ApplyPolicyToBoard(CaseBoardDto board, AgentStepDto step)
        {
            ApplyJsonObjectToPolicy(board, step.ToolResultJson);
            ApplyJsonObjectToPolicy(board, step.Message);
            if (!string.IsNullOrWhiteSpace(board.Policy.PolicyNumber) && board.Policy.Id is not null)
                board.Policy.Status = "created";
        }

        private static void ApplyClaimToBoard(CaseBoardDto board, AgentStepDto step)
        {
            ApplyJsonObjectToClaim(board, step.ToolResultJson);
            ApplyJsonObjectToClaim(board, step.Message);
            if (!string.IsNullOrWhiteSpace(board.Claim.ClaimNumber))
                board.Claim.Status = "created";
        }

        private static void ApplyJsonObjectToClaimant(CaseBoardDto board, string? json)
        {
            var wanted = board.Claimant.FullName;
            foreach (var doc in EnumerateJsonObjects(json))
            {
                var fullName = ReadString(doc, "FullName");
                if (!string.IsNullOrWhiteSpace(wanted) && !string.IsNullOrWhiteSpace(fullName)
                    && !fullName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                board.Claimant.FullName = fullName ?? board.Claimant.FullName;
                board.Claimant.Email = ReadString(doc, "Email") ?? board.Claimant.Email;
                board.Claimant.Phone = ReadString(doc, "Phone") ?? board.Claimant.Phone;
                board.Claimant.Id = ReadInt(doc, "ClaimantId") ?? board.Claimant.Id;
            }
        }

        private static void ApplyJsonObjectToPolicy(CaseBoardDto board, string? json)
        {
            var wanted = board.Policy.PolicyNumber;
            foreach (var doc in EnumerateJsonObjects(json))
            {
                var number = ReadString(doc, "PolicyNumber");
                if (!string.IsNullOrWhiteSpace(wanted) && !string.IsNullOrWhiteSpace(number)
                    && !number.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                board.Policy.PolicyNumber = number ?? board.Policy.PolicyNumber;
                board.Policy.HolderName = ReadString(doc, "HolderName") ?? board.Policy.HolderName;
                board.Policy.Product = ReadString(doc, "ProductName") ?? board.Policy.Product;
                board.Policy.Id = ReadInt(doc, "PolicyId") ?? board.Policy.Id;
                if (doc.RootElement.TryGetProperty("Premium", out var p) && p.ValueKind == JsonValueKind.Number)
                    board.Policy.Premium = p.GetDecimal();
            }
        }

        private static void ApplyJsonObjectToClaim(CaseBoardDto board, string? json)
        {
            var wanted = board.Claim.ClaimNumber;
            foreach (var doc in EnumerateJsonObjects(json))
            {
                if (ReadString(doc, "error") is not null)
                    continue;
                var number = ReadString(doc, "ClaimNumber");
                if (!string.IsNullOrWhiteSpace(wanted) && !string.IsNullOrWhiteSpace(number)
                    && !number.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                board.Claim.ClaimNumber = number ?? board.Claim.ClaimNumber;
                board.Claim.Id = ReadInt(doc, "ClaimId") ?? board.Claim.Id;
                if (doc.RootElement.TryGetProperty("Amount", out var p) && p.ValueKind == JsonValueKind.Number)
                    board.Claim.Amount = p.GetDecimal();
            }
        }

        private static IEnumerable<JsonDocument> EnumerateJsonObjects(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;
            var startArr = text.IndexOf('[');
            var startObj = text.IndexOf('{');
            if (startArr >= 0 && (startObj < 0 || startArr < startObj))
            {
                var endArr = text.LastIndexOf(']');
                if (endArr > startArr)
                {
                    JsonDocument? arr = null;
                    try { arr = JsonDocument.Parse(text[startArr..(endArr + 1)]); } catch { /* ignore */ }
                    if (arr is not null && arr.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.RootElement.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.Object)
                                yield return JsonDocument.Parse(el.GetRawText());
                        }
                        yield break;
                    }
                }
            }

            TryJson(text, out var doc);
            if (doc is not null)
                yield return doc;
        }

        private static string? ExtractPolicyNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (Regex.IsMatch(text.Trim(), @"^[A-Za-z0-9][A-Za-z0-9_\-]{1,40}$"))
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

        private static string ExtractClaimantName(string text)
        {
            var m = Regex.Match(text, @"claimant(?:\s+name)?\s*(?:[:\-]+|\bis\b)\s*([^,;\n]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string ExtractClaimNumber(string text)
        {
            var m = Regex.Match(text, @"claim\s*number\s*(?:[:\-]+|\bis\b)\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static decimal? ExtractAmount(string text)
        {
            var m = Regex.Match(text, @"amount\s*[:\-]?\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
            if (m.Success && decimal.TryParse(m.Groups[1].Value, out var amount))
                return amount;
            return null;
        }

        private static void TryJson(string? text, out JsonDocument? doc)
        {
            doc = null;
            if (string.IsNullOrWhiteSpace(text))
                return;
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return;
            try { doc = JsonDocument.Parse(text[start..(end + 1)]); } catch { /* model prose */ }
        }

        private static string? ReadString(JsonDocument? doc, string name)
        {
            if (doc is null || !doc.RootElement.TryGetProperty(name, out var p))
                return null;
            return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        }

        private static int? ReadInt(JsonDocument? doc, string name)
        {
            if (doc is null || !doc.RootElement.TryGetProperty(name, out var p))
                return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n))
                return n;
            return int.TryParse(p.ToString(), out var i) ? i : null;
        }

        private static string BuildSummary(CaseBoardDto board, List<AgentStepDto> steps)
        {
            var parts = new List<string>();
            foreach (var s in steps.Where(x => x.Status is "done" or "needs_input" or "failed"))
                parts.Add($"{s.DisplayName}: {s.Status} — {Trim(s.Message, 180)}");
            if (!string.IsNullOrWhiteSpace(board.Claimant.FullName))
                parts.Add($"Claimant {board.Claimant.FullName} #{board.Claimant.Id}");
            if (!string.IsNullOrWhiteSpace(board.Policy.PolicyNumber))
                parts.Add($"Policy {board.Policy.PolicyNumber}");
            if (!string.IsNullOrWhiteSpace(board.Claim.ClaimNumber))
                parts.Add($"Claim {board.Claim.ClaimNumber}");
            return parts.Count == 0 ? "No specialist ran." : string.Join(" | ", parts);
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) || s.Length <= n ? s : s[..n] + "…";

        private void AddChatModel(IKernelBuilder builder)
        {
            var provider = (_config["Agent:Llm:Provider"]
                ?? (string.IsNullOrWhiteSpace(_config["Agent:AzureOpenAI:Endpoint"]) ? "OpenAI" : "AzureOpenAI"))
                .Trim();

            if (provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddAzureOpenAIChatCompletion(
                    _config["Agent:Llm:AzureDeployment"] ?? _config["Agent:AzureOpenAI:Deployment"] ?? "gpt-4o-mini",
                    _config["Agent:Llm:AzureEndpoint"] ?? _config["Agent:AzureOpenAI:Endpoint"] ?? "",
                    _config["Agent:Llm:ApiKey"] ?? _config["Agent:AzureOpenAI:ApiKey"] ?? "");
                return;
            }

            var apiKey = (_config["Agent:Llm:ApiKey"] ?? _config["Agent:OpenAI:ApiKey"] ?? "").Trim();
            var model = _config["Agent:Llm:Model"] ?? _config["Agent:OpenAI:Model"] ?? "gpt-4o-mini";
            var baseUrl = (_config["Agent:Llm:BaseUrl"] ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Agent:Llm:ApiKey is empty.");

            if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = "https://api.groq.com/openai/v1";
                if (model is "gpt-4o-mini" or "")
                    model = "llama-3.1-8b-instant";
            }
            else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = "https://openrouter.ai/api/v1";
            }

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                builder.AddOpenAIChatCompletion(model, new Uri(baseUrl), apiKey);
                return;
            }

            builder.AddOpenAIChatCompletion(model, apiKey);
        }

        private async Task<AgentChatResponse> ForwardPythonAsync(string message, string accessToken, CancellationToken cancellationToken)
        {
            var python = (_config["Agent:PythonBaseUrl"] ?? "http://localhost:5290").TrimEnd('/');
            var client = _http.CreateClient("AgentPython");
            using var req = new HttpRequestMessage(HttpMethod.Post, python + "/chat")
            {
                Content = JsonContent.Create(new AgentChatRequest { Message = message })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.SendAsync(req, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new AgentChatResponse { Reply = $"Python agent HTTP {(int)response.StatusCode}: {body}" };

            return JsonSerializer.Deserialize<AgentChatResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AgentChatResponse { Reply = body };
        }
    }

    file sealed class ToolNameFilter : IFunctionInvocationFilter
    {
        private readonly List<string> _names;
        private readonly AgentToolLogStore _log;
        private readonly string _agentName;

        private readonly List<string>? _results;

        public ToolNameFilter(List<string> names, AgentToolLogStore log, string agentName = "Single", List<string>? results = null)
        {
            _names = names;
            _log = log;
            _agentName = agentName;
            _results = results;
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            var name = $"{context.Function.PluginName}.{context.Function.Name}";
            _names.Add(name);

            var requestJson = SerializeArguments(context.Arguments);
            var sw = Stopwatch.StartNew();
            var succeeded = true;
            string? responseJson = null;
            string? details = null;
            try
            {
                await next(context);
                responseJson = SerializeResult(context.Result);
                if (!string.IsNullOrWhiteSpace(responseJson))
                    _results?.Add(responseJson);
            }
            catch (Exception ex)
            {
                succeeded = false;
                details = ex.ToString();
                throw;
            }
            finally
            {
                sw.Stop();
                details = succeeded
                    ? $"agent={_agentName}; plugin={context.Function.PluginName}; tool={context.Function.Name}; durationMs={sw.ElapsedMilliseconds}"
                    : details;
                await _log.WriteAsync(
                    context.Function.PluginName ?? "",
                    context.Function.Name,
                    requestJson,
                    responseJson,
                    details,
                    (int)sw.ElapsedMilliseconds,
                    succeeded,
                    _agentName);
            }
        }

        private static string SerializeArguments(KernelArguments args)
        {
            try
            {
                var map = args.ToDictionary(x => x.Key, x => x.Value);
                return JsonSerializer.Serialize(map);
            }
            catch
            {
                return args.ToString() ?? "";
            }
        }

        private static string? SerializeResult(FunctionResult? result)
        {
            if (result is null)
                return null;
            try
            {
                var value = result.GetValue<object>();
                return value is string s ? s : JsonSerializer.Serialize(value);
            }
            catch
            {
                return result.ToString();
            }
        }
    }
}