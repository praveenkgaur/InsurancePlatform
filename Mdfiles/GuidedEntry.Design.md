# Assisted guided data entry — plan

**Status: plan only. Do not implement until you ask to build.**

Related:

- Chat + watermark intake stays on `/assistant` — `Mdfiles/Assistant.GuidedIntake.Design.md`
- Multi-agent create path — `Mdfiles/MultiAgent.Assistant.Design.md`

This page is **not** chat. It is a **wizard**: one question (or a short question group) at a time, **dropdowns where a list exists**, text where the user must type a number or name. SQL create happens **per step**: claimant row, then policy row, then claim row.

---

## Why another page

| `/assistant` | `/guided-entry` (this page) |
|---|---|
| One box, free text, Semantic Kernel | Form questions + options |
| Coordinator parses SKPK / POL / CLAIM | User picks or types fields on the page |
| Agents may invent or reuse the wrong row | Create uses the control values only |
| Watermark changes by step | Stepper + Next; Next disabled until the step is valid |

Existing `/claimants/new`, `/policies`, `/claims/new` stay. Those are **separate** screens. Guided entry is one flow that **locks** claimant → policy → claim so a claim cannot be filed without the two rows just created.

---

## Route and nav

| Item | Value |
|---|---|
| Page | `Insurance.UI.BlazorWasm/Pages/GuidedEntry.razor` |
| Route | `/guided-entry` |
| Auth | `[Authorize]` (same cookie as other work pages) |
| Nav | Work section, next to Assistant: **Guided entry** |
| Title | Guided data entry |

Do not host this on BFF. Do not put JWT in the browser.

---

## What the user should see

Left: **stepper** (1 Claimant · 2 Policy · 3 Claim · Done).  
Right: **case strip** (name, claimant #, policy number, claim number) as each create succeeds.

Each step shows:

- **Question** (one sentence)
- **Control**: text, number, or **dropdown**
- Help line (example format)
- **Next** (creates this entity, then unlocks the next step)
- **Back** only before that step’s create; after create, Back is off for that entity (ids stay)

While Next is in flight, disable the form. Show the id/number returned.

---

## Step 1 — Claimant (create first)

Question: *Who is the person for this case?*

| Field | Control | Options / rules |
|---|---|---|
| Mode | Dropdown | `Create new` \| `Use existing` |
| Existing person | Dropdown (if Use existing) | `GET` claimants: `FullName` + id. Selecting one **skips Create** and locks that id. |
| Full name | Text (if Create new) | Required. Example: SKPK. Guardrails 2–80 chars. |
| Email | Text, optional | Valid email if filled. |
| Phone | Text, optional | 10–15 digits if filled. |

**Next** → `POST` create claimant **or** keep selected existing id.  
On success: lock `ClaimantId` + `FullName`. Go to step 2.  
Do **not** create a policy yet.

---

## Step 2 — Policy (after claimant)

Question: *What policy belongs to this claimant?*

Holder is **not** a free-typed stranger: default to the locked claimant name (read-only). Optional dropdown later if one person can hold many names — v1: read-only holder.

| Field | Control | Options / rules |
|---|---|---|
| Mode | Dropdown | `Create new` \| `Use existing for this holder` |
| Existing policy | Dropdown (if Use existing) | Policies whose `HolderName` matches the locked claimant (case-insensitive). Value = `PolicyNumber`. |
| Policy number | Text (if Create new) | Example: `SKPK_POL_1`. Same pattern as guardrails. |
| Product | **Dropdown** | From BFF list (not free text): Motor, Health, Home, Life, Auto. |
| Premium | Number | > 0. No silent 8000 if the box is empty — stay on step 2. |
| Status | Dropdown | `Active` (default) \| `Inactive`. Claim step needs Active. |

**Next** → create policy **or** reuse selected number.  
On success: lock `PolicyNumber` + product. Go to step 3.  
Do **not** create a claim yet.

---

## Step 3 — Claim (after policy)

Question: *File the claim against this policy and this person?*

| Field | Control | Options / rules |
|---|---|---|
| Policy | Dropdown, **locked to this case** | One option: the policy just chosen/created. Do not list the whole book of business. |
| Claimant | Dropdown, **locked to this case** | One option: the claimant from step 1. |
| Claim number | Text | Example: `SKPK_CLAIM_01`. Optional empty = API auto-number, but prefer typed number so the strip shows what they asked for. |
| Amount | Number | Required, > 0. No silent 1000. |
| (Later) Cause / type | Dropdown | Only if Claims API gains a type field. v1: omit. |

**Next** → `POST` create claim with locked policy number + claimant id + amount.  
On success: step **Done**. Show ids. Button **Start another case** (clears strip). Links: Claims list, Assistant (optional).

---

## Dropdown source of truth

Do not hardcode products only on the Razor page if BFF already has `Agent:Guardrails:AllowedProducts`.

**v1 (simpler):** Razor uses the same five products as guardrails; keep a comment to stay in sync.

**v1.1 (preferred when we build):** `GET api/agent/guided-entry/meta` returns:

```text
Products: Motor, Health, Home, Life, Auto
PolicyStatuses: Active, Inactive
ClaimantMode: CreateNew, UseExisting
PolicyMode: CreateNew, UseExisting
```

UI binds `<select>` to those arrays. Adding a product is config, not a UI compile.

Reuse existing list APIs already used by Add claim / Policies:

- Claimants: `ClaimsService.GetClaimantsAsync`
- Policies: `PoliciesService.GetAsync`
- Creates: `CreateClaimantAsync`, `PoliciesService.CreateAsync`, `ClaimsService.CreateAsync`

Same BFF cookie proxy. **No Semantic Kernel** on this page. **No** `POST api/agent/chat`.

---

## Create order (hard rule)

```text
1. Claimant Next  →  InsuranceClaimants row (or reuse id)
2. Policy Next    →  policy row, HolderName = locked claimant
3. Claim Next     →  claim row, PolicyNumber + ClaimantId from this wizard only
```

If step 2 or 3 fails, stay on that step; do not roll back the previous row (show “claimant already saved: #id”). User can retry policy/claim.

---

## Suggested files (when we implement)

| File | Change |
|---|---|
| `Pages/GuidedEntry.razor` | New wizard page |
| `Layout/NavMenu.razor` | Link **Guided entry** |
| `Insurance.Contracts/Agent/` | Optional `GuidedEntryMetaDto` (products, statuses) |
| `AgentController` | Optional `GET guided-entry/meta` |
| `ClaimsService` / `PoliciesService` | Reuse; no new WASM HTTP client unless meta is added |

Do **not** merge this into `Assistant.razor`. Do **not** replace `/claims/new`.

---

## Example session

```text
Nav → Guided entry
Step 1: Mode = Create new. Name SKPK. Next → Claimant #10xx
Strip: SKPK #10xx

Step 2: Mode = Create new. Number SKPK_POL_1. Product dropdown Motor. Premium 8000. Next
Strip: SKPK #10xx | SKPK_POL_1 Motor

Step 3: Policy dropdown shows only SKPK_POL_1. Claimant dropdown only SKPK. Number SKPK_CLAIM_01. Amount 5000. Next
Strip: all three + claim id
Done.
```

Reuse path:

```text
Step 1: Mode = Use existing. Dropdown Anita Sharma. Next (no insert)
Step 2: Mode = Use existing. Dropdown POL-2001 (holder Anita). Next
Step 3: File claim against that pair.
```

---

## What we will not do in v1

- Chat box or kernels on this page
- Three separate Blazor sites
- Creating policy/claim before claimant Next succeeds
- Listing every policy in the claim dropdown
- Changing Azure hosts
- Replacing Assistant guided **chat** intake

---

## Implementation order (when you say build)

1. Add `GuidedEntry.razor` + nav link; stepper UI; empty case strip.  
2. Step 1: mode dropdown, name fields, create or select claimant; lock id.  
3. Step 2: product/status dropdowns, holder locked, create or select policy; lock number.  
4. Step 3: locked policy + claimant dropdowns, amount, create claim.  
5. Done screen + Start another.  
6. Optional: `GET guided-entry/meta` so dropdown options come from BFF config.  
7. Verify in browser: new claimant → new policy → new claim; then existing claimant → existing policy → claim.

---

## Decision needed from you

Proceed with **wizard page `/guided-entry`**, **dropdowns for lists**, **create per step** (claimant then policy then claim), **no SK on this page**.

Say when to implement this file in code.
