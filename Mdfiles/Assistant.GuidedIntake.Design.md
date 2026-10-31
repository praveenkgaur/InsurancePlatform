# Assistant guided intake

Implemented. Related:

- Multi-agent create path — `Mdfiles/MultiAgent.Assistant.Design.md`
- Form wizard (questions + dropdowns, not chat) — `Mdfiles/GuidedEntry.Design.md` (**plan only**)

| Function | File | Role |
|---|---|---|
| `GuidedIntake.Start` | `Authentication.Bff.Api/Agent/GuidedIntake.cs` | First watermark (claimant). Used by `GET api/agent/meta`. |
| `GuidedIntake.ForStep` | same | Watermark / help / prompt for Claimant, Policy, Claim, Run, Done. |
| `GuidedIntake.CollectIntake` | same | Parse the message, fill the case board, return `ReadyToRun`. |
| `GuidedIntake.IsReadyToRun` | same | True only when name + policy number + claim number + amount (if required) are on the board. |
| `FinishGuidedIntake` | `AgentChatService.cs` | Return coordinator `needs_input` — **no** specialist kernels. |
| `RunMultiAsync` | `AgentChatService.cs` | Calls `CollectIntake` first; specialists only when ready. |

---

## Why

Free-text “create a claim” reused the first DB row (Anita Sharma, POL-2001) instead of numbers you typed (SKPK, SKPK_POL_1, SKPK_CLAIM_01).

A claim needs a **person**, then a **policy**, then a **claim**. The UI asks in that order, then starts tools.

---

## What the user should see

| Step | Watermark | Help |
|---|---|---|
| 1 Claimant | `Claimant full name. Example: SKPK` | Step 1 of 3 — person first. Optional: email, phone. |
| 2 Policy | `Policy number. Example: SKPK_POL_1` | Step 2 of 3 — holder defaults to the claimant on the board. Optional: product, premium. |
| 3 Claim | `Claim number and amount. Example: SKPK_CLAIM_01 amount 5000` | Step 3 of 3 — then Create runs. |
| Running | `Agents are creating claimant, policy, claim…` | Specialists creating from the case board. |
| Done | `Ask another case, or type claimant / policy / claim numbers` | Board shows the three numbers. |

Do not default amount to 1000 on this path. `Agent:Intake:Enabled: false` restores one-shot Multi (keywords + default amount).

---

## Config — `Authentication.Bff.Api/appsettings.Development.json`

```json
"Agent": {
  "Intake": {
    "Enabled": true,
    "RequireAmount": true,
    "DefaultProduct": "Motor"
  }
}
```

---

## Contracts — `Insurance.Contracts/Agent/AgentChatRequest.cs`

```csharp
public sealed class AgentChatResponse
{
    public string Mode { get; set; } = "Single";
    public string Reply { get; set; } = "";
    public string[] ToolsCalled { get; set; } = [];
    public CaseBoardDto CaseBoard { get; set; } = new();
    public List<AgentStepDto> Steps { get; set; } = [];
    public AgentIntakeDto Intake { get; set; } = new();
}

public sealed class AgentIntakeDto
{
    public string Step { get; set; } = "Claimant";
    public string Prompt { get; set; } = "";
    public string Watermark { get; set; } = "";
    public string Help { get; set; } = "";
}

public sealed class AgentMetaResponse
{
    public string Mode { get; set; } = "Single";
    public string Orchestrator { get; set; } = "";
    public List<AgentMetaItem> Agents { get; set; } = [];
    public AgentIntakeDto Intake { get; set; } = new();
}
```

`AgentChatRequest` is still message + history + case board. Intake is written on the **response** (and on meta).

---

## BFF — `GuidedIntake.cs` (both functions)

```csharp
public static AgentIntakeDto Start() => ForStep(StepClaimant);

public static AgentIntakeDto ForStep(string step, string? prompt = null) =>
    step switch
    {
        StepPolicy => new AgentIntakeDto
        {
            Step = StepPolicy,
            Prompt = prompt ?? "Claimant is on the board. What is the policy number? Example: SKPK_POL_1",
            Watermark = "Policy number. Example: SKPK_POL_1",
            Help = "Step 2 of 3 — holder defaults to the claimant on the board. Optional: product, premium."
        },
        StepClaim => new AgentIntakeDto
        {
            Step = StepClaim,
            Prompt = prompt ?? "Policy is on the board. Claim number and amount? Example: SKPK_CLAIM_01 amount 5000",
            Watermark = "Claim number and amount. Example: SKPK_CLAIM_01 amount 5000",
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
            Prompt = prompt ?? "What is the claimant full name? Example: SKPK",
            Watermark = "Claimant full name. Example: SKPK",
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
        return Incomplete(board, StepClaimant, "What is the claimant full name? Example: SKPK");

    var nameGuard = guardrails.Claimant(board.Claimant.FullName, board.Claimant.Email, board.Claimant.Phone);
    if (!nameGuard.Ok)
    {
        board.Claimant.FullName = "";
        return Incomplete(board, StepClaimant, nameGuard.Message + " Example: SKPK");
    }

    if (string.IsNullOrWhiteSpace(board.Policy.PolicyNumber))
        return Incomplete(
            board,
            StepPolicy,
            $"Claimant {board.Claimant.FullName} is on the board. What is the policy number? Example: SKPK_POL_1");

    var holder = string.IsNullOrWhiteSpace(board.Claimant.FullName) ? "Pending" : board.Claimant.FullName;
    var product = string.IsNullOrWhiteSpace(board.Policy.Product) ? "Motor" : board.Policy.Product;
    var premium = board.Policy.Premium is > 0 ? board.Policy.Premium.Value : 8000m;
    var policyGuard = guardrails.Policy(board.Policy.PolicyNumber, holder, product, premium);
    if (!policyGuard.Ok)
    {
        board.Policy.PolicyNumber = "";
        return Incomplete(board, StepPolicy, policyGuard.Message + " Example: SKPK_POL_1");
    }

    if (string.IsNullOrWhiteSpace(board.Claim.ClaimNumber) || (requireAmount && board.Claim.Amount is null or <= 0))
        return Incomplete(
            board,
            StepClaim,
            $"Policy {board.Policy.PolicyNumber} noted (holder {holder}). Claim number and amount? Example: SKPK_CLAIM_01 amount 5000");

    return new GuidedIntakeResult
    {
        Board = board,
        ReadyToRun = true,
        Intake = ForStep(StepRun, "All fields present. Assigning Claimant agent → Policy agent → Claim agent.")
    };
}

public static bool IsReadyToRun(GuidedIntakeResult result) => result.ReadyToRun;
```

Parsing helpers live in the same file: `ApplyGuidedIntakeMessage`, `ExtractPolicyNumber` (`POL` in the token), `ExtractClaimNumber` (`CLAIM` / `CLM`), `ExtractAmount` (`amount 5000`). Bare tokens: `SKPK`, `SKPK_POL_1`, `SKPK_CLAIM_01`.

---

## BFF — `AgentChatService.RunMultiAsync` (intake then specialists)

```csharp
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

var wantClaimant = intakeEnabled || /* keyword fallback when intake off */;
var wantPolicy = intakeEnabled || /* ... */;
var wantClaim = intakeEnabled || /* ... */;
```

If intake is on and amount is still missing after specialists start:

```csharp
if (amount is null or <= 0)
{
    if (intakeEnabled)
    {
        intake = GuidedIntake.ForStep(
            GuidedIntake.StepClaim,
            $"Policy {board.Policy.PolicyNumber} is ready. Claim number and amount? Example: SKPK_CLAIM_01 amount 5000");
        return FinishGuidedIntake(new GuidedIntakeResult { Board = board, Intake = intake, ReadyToRun = false });
    }
    amount = 1000m; // only when Intake:Enabled is false
}
```

Successful Multi finish:

```csharp
return FinishMulti(reply, board, steps, allTools, GuidedIntake.ForStep(GuidedIntake.StepDone, reply));
```

```csharp
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
```

---

## BFF — `AgentController` meta

```csharp
return Ok(new AgentMetaResponse
{
    Mode = mode,
    Orchestrator = _config["Agent:Orchestrator"] ?? "Coordinator",
    Intake = GuidedIntake.Start(),
    Agents = [ /* Claimant, Policy, Claim */ ]
});
```

---

## UI — `Insurance.UI.BlazorWasm/Pages/Assistant.razor`

```razor
<input class="form-control"
       placeholder="@Watermark"
       @bind="draft"
       @bind:event="oninput"
       disabled="@busy" />
<p class="form-text mt-2">@HelpText</p>
```

```csharp
private AgentIntakeDto intake = new()
{
    Step = "Claimant",
    Watermark = "Claimant full name. Example: SKPK",
    Help = "Step 1 of 3 — person first. Optional: email, phone."
};

private string Watermark =>
    busy && IsMulti
        ? "Agents are creating claimant, policy, claim…"
        : (string.IsNullOrWhiteSpace(intake.Watermark)
            ? "Claimant full name. Example: SKPK"
            : intake.Watermark);

private string HelpText =>
    string.IsNullOrWhiteSpace(intake.Help)
        ? "Step 1 of 3 — person first. Optional: email, phone."
        : intake.Help;

protected override async Task OnInitializedAsync()
{
    meta = await Agent.MetaAsync();
    if (meta?.Intake is not null && !string.IsNullOrWhiteSpace(meta.Intake.Watermark))
        intake = meta.Intake;
}

// after ChatAsync:
board = result.CaseBoard ?? new CaseBoardDto();
if (result.Intake is not null && !string.IsNullOrWhiteSpace(result.Intake.Step))
    intake = result.Intake;
```

`AgentApi.ChatAsync` still posts `Message`, `History`, `CaseBoard` — no extra request field.

---

## Example session

```text
UI watermark: Claimant full name. Example: SKPK
You: SKPK
Coordinator: Claimant SKPK is on the board. What is the policy number? Example: SKPK_POL_1
Board: Claimant SKPK | Policy — | Claim —

You: SKPK_POL_1
Coordinator: Policy SKPK_POL_1 noted (holder SKPK). Claim number and amount? Example: SKPK_CLAIM_01 amount 5000
Board: Claimant SKPK | Policy SKPK_POL_1 | Claim —

You: SKPK_CLAIM_01 amount 5000
Coordinator: All fields present. Assigning specialists.
Claimant agent: created SKPK
Policy agent: created SKPK_POL_1
Claim agent: created SKPK_CLAIM_01
Board: all three numbers + ids
```

One-shot still valid:

```text
You: claimant SKPK, policy SKPK_POL_1, claim SKPK_CLAIM_01 amount 5000
Coordinator: All fields present. Assigning specialists.
```

---

## What we will not do

- Three chat boxes or three Blazor pages
- Kernels in WASM
- Starting Policy/Claim SQL create during step 1
- Changing Azure hosts
- Default amount 1000 while `Agent:Intake:Enabled` is true
