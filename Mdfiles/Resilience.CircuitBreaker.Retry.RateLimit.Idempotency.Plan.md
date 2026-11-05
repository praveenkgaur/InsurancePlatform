# Plan: Resilience — Circuit breaker, Retry, Rate limiting, Idempotency

Status: **implemented in code** (2026-11-05). Local `dotnet build InsurancePlatform.sln` succeeded. MudBlazor / MetaControls / DataAccess rename is **parked**.

Azure **pk-ins-bff** has the four `Resilience__*__Enabled` flags. They apply only **after you publish** this BFF build. Until then the portal keys sit unused on the old deployment.

ChatGPT thread `https://chatgpt.com/c/6aeaee05-3c28-83ec-aabd-cbff78453bee` could not be opened from here (login-only). This plan follows those four ideas on **this** solution (BFF cookie, in-process claims, Function hook).

The UI still talks **only to the BFF**. No JWT or Function keys in WASM.

---

## Definitions — what they are, what they do, how we implement them

These four are **resilience** patterns: the app keeps working (or fails fast and safely) when the network, Azure SQL, or the Function App misbehaves. They are **not** authentication. Cookie + JWT stay as they are.

| Pattern | One-line definition |
| --- | --- |
| **Retry** | Try the **same** call again after a **short wait** if the failure looks **temporary**. |
| **Circuit breaker** | After **too many** failures, **stop calling** for a while (fail fast), then try again. |
| **Rate limiting** | Allow only **N requests per time window** per user/IP so one client cannot flood the BFF. |
| **Idempotency** | The **same** Save (same key + same body) runs **once**; a repeat returns the **stored** result. |

Packages / platform pieces used in the paste guide:

| Piece | NuGet or built-in | Used for |
| --- | --- | --- |
| `Microsoft.Extensions.Http.Resilience` | NuGet on `Insurance.Helpers` | **Retry + circuit breaker + timeouts** on `HttpClient` (`AddStandardResilienceHandler`) |
| Polly (comes with that package) | Transitive | Backoff, circuit state (closed / open / half-open) |
| `Microsoft.AspNetCore.RateLimiting` | Built into ASP.NET Core (`AddRateLimiter`) | **Rate limiting** on BFF inbound HTTP |
| `System.Threading.RateLimiting` | Built-in | `FixedWindowLimiter` partitions |
| `SqlCommandExecutor` + `dbo.IdempotencyRecord` | Our SQL | **Idempotency** store |
| `ClaimantCreatedClient` | Our code | Function POST; retries/circuit wrap this `HttpClient` |

**Services that get this (hosts, not new Azure products):**

| Host | Retry | Circuit | Rate limit | Idempotency |
| --- | --- | --- | --- | --- |
| **pk-ins-bff** (`Authentication.Bff.Api`) | Yes — Function + Gateway + agent `HttpClient`s | Yes — same pipelines | Yes — inbound `/api/*` | Yes — POST claimants/claims/policies |
| **Claims.Api** (`pk-ins-claims`) | Yes — Function `HttpClient` only | Yes — same | **No** — not wired in `Claims.Api/Program.cs` | **No** — middleware is BFF-only |
| **WASM UI** | No POST retry | No | No (server enforces) | Sends header `Idempotency-Key` only |
| **pk-ins-fn-claimants** | Azure host retries internally; our C# does not add Polly inside the Function | — | Function **key** is auth, not rate limit | Not the source of truth for “one claimant” |

No new App Service. No Redis. No APIM (you have `UseApim: false`).

---

### Retry

**Meaning:** If a call fails for a reason that might succeed **a second later**, wait and call again. If the request is **wrong** (400) or **forbidden** (401/403), do **not** retry.

**What it is doing:** Hides blips: Function cold start, Azure SQL **paused** then waking, 502 from App Service, 429 from a downstream, dropped TCP.

**How we implement it:**

1. **HTTP:** `AddInsuranceHttpResilience` → `AddStandardResilienceHandler` on:
   - `ClaimantCreatedClient` (BFF and Claims.Api) — POST to the Function
   - named client `"Gateway"` — BFF → Claims/Policies/Billing when not in-process
   - `"AgentClaims"`, `"AgentPolicies"`, `"AgentPython"`
2. Policy: max **3** retries, exponential delay (~0.4s, 0.8s, 1.6s). Standard handler retries **5xx**, timeouts, `HttpRequestException`. It does **not** retry 400/401/403.
3. **SQL:** `SqlCommandExecutor.WithRetry` — up to 3 tries on Azure SQL **transient** numbers (e.g. 40613 database unavailable / pause). Unique-key failures are **not** retried.
4. **WASM:** we **do not** retry POST from the browser (that would create two claimants until idempotency exists).

Flag: `Resilience:Retry:Enabled` / `Resilience__Retry__Enabled`.

---

### Circuit breaker

**Meaning:** Named after an electrical breaker. If a dependency is **down**, stop sending traffic so you do not wait on every request. After a **break** period, allow a trial call (**half-open**). Success → **closed** (normal). Failure → **open** again.

**What it is doing:** When `pk-ins-fn-claimants` is Unhealthy (missing `AzureWebJobsStorage`) or the unique hostname is wrong, the BFF should **not** hang 10s on every Save. Fail fast, log, **still insert the claimant** (`NotifyAsync` already swallows errors).

**How we implement it:** Same `AddStandardResilienceHandler` (it includes a circuit). Separate **pipelines** per `HttpClient`:

| Pipeline (client) | Protects |
| --- | --- |
| `ClaimantCreatedClient` | HTTP Function |
| `"Gateway"` | pk-ins-claims / policies / billing |
| Agent clients | SK HTTP to APIs / Python |

Do **not** share one breaker for Function and Claims. Config: `MinimumThroughput` 5, `BreakSeconds` 30. Flag: `Resilience:CircuitBreaker:Enabled`.

States: **Closed** = calls go through (retries still apply) → **Open** = fail immediately → **Half-open** = one probe.

---

### Rate limiting

**Meaning:** Cap how many HTTP requests a **caller** may make in a **time window**. Extra calls get **429 Too Many Requests**.

**What it is doing:** Stops a stuck UI loop, a script, or one user from flooding `POST /api/claimants`. Protects Azure SQL and the Function from being woken constantly (cost + load).

**How we implement it:** ASP.NET **`AddRateLimiter`** + **`UseRateLimiter`** on **BFF only** (`BffRateLimiterExtensions`).

- **Partition:** session cookie (`AppSession` / `__Host-AppSession`) or client IP
- **POST/PUT/DELETE:** 30 per 60 seconds (config `PermitLimit` / `WindowSeconds`)
- **GET `/api/*`:** 4× that (reads are cheaper)
- **`/health` and `/swagger`:** no limit
- Body on reject: `{"message":"Too many requests. Wait a minute and try again."}`

This is **inbound** (browser → BFF). It is **not** the Function key and **not** Entra.

Flag: `Resilience:RateLimit:Enabled`.

---

### Idempotency

**Meaning:** Performing the **same** operation **more than once** has the **same effect** as doing it **once**. HTTP GET is naturally idempotent. **POST Save** is not, unless we add a key.

**What it is doing:** Double-click Save, or the user retries after a timeout, must **not** insert two claimants.

**How we implement it:**

1. SQL `dbo.IdempotencyRecord` (`Sql/009_Idempotency.sql`)
2. WASM `ClaimsService.CreateClaimantAsync` sends header **`Idempotency-Key`**: `Guid.NewGuid()` **per click** (a new form submit = new key = new row, which is correct)
3. BFF `IdempotencyMiddleware` (`Insurance.Helpers`, `app.UseMiddleware` in BFF `Program.cs`) on POST `/api/claimants`, `/api/claims`, `/api/policies`:
   - no header → proceed (old clients)
   - key seen, **same** body hash → return **stored** status/body, **no** second INSERT
   - key seen, **different** body → **409**
   - new key → run action, save response if 2xx
4. Store: `IdempotencyStore` on `SqlCommandExecutor` (same Azure SQL `DemoDataBase`)

The Function is **not** the idempotency boundary. Two hooks with the same claimant are OK; **one SQL claimant** is the rule.

**Why SQL (`IdempotencyRecord`) from middleware:** a timeout or double POST must not insert twice. The table stores key + body hash + 2xx body. Same key + same body → **replay**, skip `ClaimsService`. Same key + different body → **409**. A **new GUID per UI click** is two keys = two claimants (intentional). In-memory store would lose keys on BFF restart.

Flag: `Resilience:Idempotency:Enabled`. Header name: `Resilience:Idempotency:HeaderName` (default `Idempotency-Key`).

---

### How they stack on Save claimant

```text
Browser  POST /api/claimants  + cookie  + Idempotency-Key
    │
    ▼
[Rate limiter]     too many POSTs → 429  (BFF, AddRateLimiter)
    │
    ▼
[Idempotency]      same key+body → replay 200, skip INSERT
    │
    ▼
[SQL retry]        Azure SQL paused → retry ExecuteAsync
    │
    ▼
INSERT InsuranceClaimants
    │
    ▼
[Function HttpClient: retry then circuit]
    ClaimantCreatedClient.NotifyAsync
    Functions:Enabled still applies
    failure → log, Save still 200
```

---

## 1. Where each pattern belongs

| Pattern | What it protects | Put it here | Do **not** |
| --- | --- | --- | --- |
| **Retry** | Blips: 408, 429, 5xx, timeout, SQL wake | BFF `HttpClient` (Gateway, Function), `SqlCommandExecutor` transient | Retry **400/401/403**; infinite retry on Save |
| **Circuit breaker** | Downstream **dead** (Claims App Service, Function NXDOMAIN) | Same HttpClients | Open the circuit on one 400 |
| **Rate limiting** | Abuse / accidental loops | **Inbound** BFF (and APIs if called directly) | Limit `/health` to 1/min |
| **Idempotency** | Double-click Save, retry after timeout | **POST** claimants/claims/policies | GET; Function-only (SQL row is source of truth) |

**Now:** Function client timeout **10s** + `AddInsuranceHttpResilience`. Gateway timeout **8s** local / **30s** Azure. Rate limiter and idempotency middleware on BFF. SQL transient retry in `SqlCommandExecutor`. WASM sends `Idempotency-Key` on create claimant.

.NET 10: **`Microsoft.Extensions.Http.Resilience`** (retry + circuit on `HttpClient`) + built-in **`AddRateLimiter`**. Idempotency = `dbo.IdempotencyRecord` + middleware.

---

## 2. Flow (Save claimant)

```text
UI Save (optional Idempotency-Key)
  → BFF rate limiter (too many POSTs → 429)
  → Idempotency store: same key + same body → return stored 200
  → INSERT claimant (SQL retry if Azure SQL is waking)
  → ClaimantCreatedClient if Functions:Enabled
        HttpClient retry (2–3 times, backoff)
        circuit: Function down → skip hook, Save still 200
  → 200 DTO to UI
```

BFF **Gateway** client (when not in-process): retry + circuit around Claims/Policies/Billing. You already **TryLocalAsync** when `UseGateway` is false — circuit is more important when HTTP to `pk-ins-claims` is used.

---

## 3. Flag-driven config (like `Functions:Enabled`)

`appsettings.json` (BFF + APIs). Secrets still empty.

```json
"Resilience": {
  "Retry": { "Enabled": true, "MaxRetries": 3 },
  "CircuitBreaker": { "Enabled": true, "MinimumThroughput": 5, "BreakSeconds": 30 },
  "RateLimit": { "Enabled": true, "PermitLimit": 30, "WindowSeconds": 60 },
  "Idempotency": { "Enabled": true, "HeaderName": "Idempotency-Key", "TtlHours": 24 }
}
```

Azure App settings on **pk-ins-bff** (portal; values not in git):

| Name | Needed |
| --- | --- |
| `Resilience__Retry__Enabled` | Yes (UI path) |
| `Resilience__CircuitBreaker__Enabled` | Yes |
| `Resilience__RateLimit__Enabled` | Yes — inbound BFF only |
| `Resilience__Idempotency__Enabled` | Yes — inbound BFF only |
| `Functions__ClaimantCreatedUrl` / `Functions__HostKey` | Yes — Function hook from BFF |
| Nested `MaxRetries`, `PermitLimit`, header name | **No** — published json defaults |

**pk-ins-claims:** Retry + circuit flags matter only if something **HTTP-calls Claims.Api** (Postman/JWT, or later `UseGateway: true`). RateLimit and Idempotency settings on Claims **do nothing** until you wire middleware there. UI does not call Claims.

Do not put `Functions__HostKey` in `appsettings.Production.json`.

---

## 4. Retry

**HTTP (`AddStandardResilienceHandler` or explicit retry):**

- Named clients: `"Gateway"`, `ClaimantCreatedClient`, `"AgentClaims"`, `"AgentPolicies"`
- Retry: `HttpRequestException`, `TimeoutRejected`, **408, 429, 500, 502, 503, 504**
- **Do not retry** 400, 401, 403, 409
- Max 3 attempts, exponential backoff (e.g. 0.5s, 1s, 2s)
- Function: client **Timeout 10s** so retries fit inside the pipeline (`AddInsuranceHttpResilience` total timeout 10s)

**SQL:** wrap `SqlCommandExecutor` (later `SqlDBAccessHelper`) for Azure SQL **transient** (pause/wake, failover). 2–3 retries. Do not retry unique-key violations.

**WASM:** optional **GET** retry only. **Do not** auto-retry POST from the browser if BFF idempotency is in place (or you double-insert until keys exist).

**Types (suggested):** `AddInsuranceHttpResilience(this IHttpClientBuilder)` in Helpers.

---

## 5. Circuit breaker

On the **same** HttpClients:

- After **N** failed calls (e.g. 5) in a window, **open** for **30s**
- Open → fail fast (no hang)
- **Function:** `ClaimantCreatedClient.NotifyAsync` already swallows errors — circuit **skips** extra POSTs while Function/storage is unhealthy
- **Claims HTTP:** if circuit open and `UseGateway` is false, keep **TryLocalAsync** (in-process SQL) so Save still works

Do not share one circuit for Function **and** Claims (one outage would block the other). **Two** breakers: `fn-claimants`, `downstream-claims`.

---

## 6. Rate limiting

ASP.NET **`AddRateLimiter`** on **BFF** (users hit this):

| Policy | Partition | Limit (start) | Routes |
| --- | --- | --- | --- |
| `api-write` | session cookie or IP | 30 / 60s | POST/PUT/DELETE `/api/*` |
| `api-read` | same | 120 / 60s | GET `/api/*` |
| `login` | IP | 10 / 60s | `/auth/login`, Entra start |
| `health` | none or very high | do not choke probes | `/health` |

Response **429** + `Retry-After`. WASM shows a short message.

Optional later: same limiter on Claims.Api if anything besides BFF calls it.

---

## 7. Idempotency

**Problem:** two clicks, or retry after 502, must not create two claimants.

**Header:** `Idempotency-Key` (GUID from WASM `Save`).

**SQL** `Sql/009_Idempotency.sql`:

```text
dbo.IdempotencyRecord
  IdempotencyKey  NVARCHAR(64) PK
  Method          NVARCHAR(10)
  Path            NVARCHAR(256)
  RequestHash     NVARCHAR(64)   -- same key + different body → 409
  StatusCode      INT
  ResponseBody    NVARCHAR(MAX)
  CreatedUtc      DATETIME2
```

**Filter** `IdempotencyFilter` on POST `/api/claimants`, `/api/claims`, `/api/policies` (BFF in-process **and** Claims.Api).

1. No header + `Idempotency:Enabled` → still allow (or require header only on UI Save later)
2. Known key, same hash → return **stored** status/body, **no** second INSERT
3. Known key, **different** hash → **409**
4. New key → run action, then insert record (TTL 24h; optional job delete)

WASM `ClaimsService.CreateClaimantAsync`: generate `Guid.NewGuid()` per Save, send header. New Save after success → **new** key.

Function POST is **not** the idempotency boundary. Duplicate hook is OK; SQL claimant is not.

---

## 8. Stepwise implementation

| Step | Work | Status |
| --- | --- | --- |
| **R0** | Package on Helpers; `Resilience` json on BFF + Claims | **Done.** Helpers `Microsoft.Extensions.Http.Resilience` **10.11.0** + `Polly.Core` **8.9.0**. JSON in BFF `appsettings.json`, `appsettings.Production.json`, Claims `appsettings.json`. |
| **R1** | `AddInsuranceHttpResilience` on `ClaimantCreatedClient` | **Done.** BFF + Claims `Program.cs`. Timeout 10s. |
| **R2** | Same handler on `"Gateway"` + agent clients | **Done.** BFF `Program.cs`. |
| **R3** | SQL transient retry in `SqlCommandExecutor` | **Done.** |
| **L1** | `AddRateLimiter` on BFF; 429 JSON | **Done.** `BffRateLimiterExtensions` + `UseRateLimiter`. |
| **I1** | SQL table + middleware + WASM header | **Code done.** Middleware lives in **`Insurance.Helpers/IdempotencyMiddleware.cs`** (not BFF folder). `UseMiddleware` in BFF after `UseAuthorization`. WASM `CreateClaimantAsync` sends a new GUID per click. Local `dbo.IdempotencyRecord` exists. Keep `Sql/009_Idempotency.sql` in the repo; run the same script on **Azure** `DemoDataBase` if you have not. |
| **F** | Azure flags; note in `Azure.Setup.StepByStep.md` | **BFF flags in portal.** Publish BFF (and Claims if you use that API) so code matches flags. |

Order was **R0 → R1 → R2 → L1 → I1 → R3**. Circuit comes with R1/R2 (bundled in Http.Resilience).

---

## 9. Out of scope for v1

- Mud / MetaControls (parked)
- Renaming Helpers to DataAccessHelpers
- Rate limit on Azure APIM (you have `UseApim: false`)
- Idempotency on GET
- Distributed Redis store (SQL is enough)

---

## 10. Confirm before coding

1. Start **R1** (Function + named HttpClient retry + circuit), then **I1** (claimant POST key).
2. WASM sends `Idempotency-Key` only after BFF/API store exists.
3. Flags default: retry/circuit **on**; rate limit **on** with loose limits; idempotency **on** for POSTs that have the header.

---

# Paste guide (copy in this order)

Code below is the **source paste**. The repo already has it, with these **as-built** differences — do not paste a second copy:

- Package version **10.11.0** (plan originally said 10.0.0). Extra `Polly.Core` 8.9.0.
- `HttpResilienceExtensions.cs` needs `using Microsoft.Extensions.Http.Resilience;` (and `using Polly;` if `DelayBackoffType` is unresolved).
- `BffRateLimiterExtensions.cs` needs `using System.Threading.RateLimiting;`, `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.RateLimiting`.
- `IdempotencyMiddleware` is in **Helpers**, namespace `Insurance.Helpers`. BFF: `app.UseMiddleware<IdempotencyMiddleware>();`
- Do **not** put `AddInsuranceHttpResilience` on the WASM `"Bff"` client.

Do **R0 → R1 → R2 → L1 → I1 → R3**. After each step, build BFF. Do not put keys in git.

---

## R0 — Config JSON (both BFF and Claims.Api)

Paste into `Authentication.Bff.Api/appsettings.json` and `Claims.Api/appsettings.json` (sibling of `"Functions"`).

```json
  "Resilience": {
    "Retry": { "Enabled": true, "MaxRetries": 3 },
    "CircuitBreaker": { "Enabled": true, "MinimumThroughput": 5, "BreakSeconds": 30 },
    "RateLimit": { "Enabled": true, "PermitLimit": 30, "WindowSeconds": 60 },
    "Idempotency": { "Enabled": true, "HeaderName": "Idempotency-Key", "TtlHours": 24 }
  },
```

Azure BFF App settings later (optional overrides): `Resilience__Retry__Enabled`, `Resilience__CircuitBreaker__Enabled`, `Resilience__RateLimit__Enabled`, `Resilience__Idempotency__Enabled`.

Package Manager Console:

```powershell
dotnet add Insurance.Helpers/Insurance.Helpers.csproj package Microsoft.Extensions.Http.Resilience --version 10.11.0
```

Helpers is referenced by BFF and Claims.Api, so both get the package.

---

## R1 — Retry + circuit on Function HttpClient

### New file `Insurance.Helpers/HttpResilienceExtensions.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Insurance.Helpers;

public static class HttpResilienceExtensions
{
    public static IHttpClientBuilder AddInsuranceHttpResilience(
        this IHttpClientBuilder builder,
        IConfiguration config,
        TimeSpan totalTimeout)
    {
        if (!config.GetValue("Resilience:Retry:Enabled", true)
            && !config.GetValue("Resilience:CircuitBreaker:Enabled", true))
            return builder;

        var retries = config.GetValue("Resilience:Retry:MaxRetries", 3);
        var minThroughput = config.GetValue("Resilience:CircuitBreaker:MinimumThroughput", 5);
        var breakSeconds = config.GetValue("Resilience:CircuitBreaker:BreakSeconds", 30);

        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = Math.Max(1, retries);
            options.Retry.Delay = TimeSpan.FromMilliseconds(400);
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            options.CircuitBreaker.ShouldHandle = args =>
                ValueTask.FromResult(args.Outcome.Exception is not null
                    || (args.Outcome.Result is { } r && (int)r.StatusCode >= 500));
            options.CircuitBreaker.MinimumThroughput = Math.Max(2, minThroughput);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(breakSeconds);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, breakSeconds));
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(2, totalTimeout.TotalSeconds / 2));
            options.TotalRequestTimeout.Timeout = totalTimeout;
        });
        return builder;
    }
}
```

If the compiler cannot find `Polly.DelayBackoffType`, add:

```powershell
dotnet add Insurance.Helpers/Insurance.Helpers.csproj package Polly.Core --version 8.5.2
```

and keep `using Polly;`. If `DelayBackoffType` is on `options.Retry` as an enum in Http.Resilience, use `options.Retry.UseJitter = true` and delete the `BackoffType` line.

### Replace HttpClient registration — `Authentication.Bff.Api/Program.cs`

Find:

```csharp
builder.Services.AddHttpClient<Claims.Api.Services.ClaimantCreatedClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(3));
```

Replace with:

```csharp
builder.Services.AddHttpClient<Claims.Api.Services.ClaimantCreatedClient>(client =>
        client.Timeout = TimeSpan.FromSeconds(10))
    .AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(10));
```

### Same in `Claims.Api/Program.cs`

Find:

```csharp
builder.Services.AddHttpClient<ClaimantCreatedClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(3));
```

Replace with:

```csharp
builder.Services.AddHttpClient<ClaimantCreatedClient>(client =>
        client.Timeout = TimeSpan.FromSeconds(10))
    .AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(10));
```

`ClaimantCreatedClient.NotifyAsync` already catches exceptions — Save still succeeds if the circuit is open.

**Check:** build BFF. Azure: Function down → BFF log warning, claimant still saved.

---

## R2 — Same handler on Gateway + agent clients

In `Authentication.Bff.Api/Program.cs` find:

```csharp
builder.Services.AddHttpClient("Gateway", client =>
    client.Timeout = TimeSpan.FromSeconds(builder.Environment.IsDevelopment() ? 3 : 30));
builder.Services.AddHttpClient("AgentClaims");
builder.Services.AddHttpClient("AgentPolicies");
builder.Services.AddHttpClient("AgentPython");
```

Replace with:

```csharp
var gatewayTimeout = TimeSpan.FromSeconds(builder.Environment.IsDevelopment() ? 8 : 30);
builder.Services.AddHttpClient("Gateway", client => client.Timeout = gatewayTimeout)
    .AddInsuranceHttpResilience(builder.Configuration, gatewayTimeout);
builder.Services.AddHttpClient("AgentClaims")
    .AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("AgentPolicies")
    .AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("AgentPython")
    .AddInsuranceHttpResilience(builder.Configuration, TimeSpan.FromSeconds(15));
```

Do **not** put this on the WASM `"Bff"` client (avoids double POST from the browser).

---

## L1 — Rate limiting (BFF only)

### New file `Insurance.Helpers/BffRateLimiterExtensions.cs`

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Insurance.Helpers;

public static class BffRateLimiterExtensions
{
    public static IServiceCollection AddInsuranceRateLimiter(this IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue("Resilience:RateLimit:Enabled", true))
            return services;

        var writeLimit = config.GetValue("Resilience:RateLimit:PermitLimit", 30);
        var window = TimeSpan.FromSeconds(config.GetValue("Resilience:RateLimit:WindowSeconds", 60));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, token) =>
            {
                ctx.HttpContext.Response.ContentType = "application/json";
                await ctx.HttpContext.Response.WriteAsync(
                    """{"message":"Too many requests. Wait a minute and try again."}""", token);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var path = http.Request.Path.Value ?? "";
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("health");

                var cookie = CookieSettings.Name(http.Request.IsHttps);
                var user = (http.Request.Cookies.TryGetValue(cookie, out var v) ? v : null)
                    ?? http.Connection.RemoteIpAddress?.ToString()
                    ?? "anon";
                var write = HttpMethods.IsPost(http.Request.Method)
                    || HttpMethods.IsPut(http.Request.Method)
                    || HttpMethods.IsDelete(http.Request.Method);
                var partition = write ? user + ":w" : user + ":r";
                var permit = write ? writeLimit : writeLimit * 4;
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(5, permit),
                    Window = window,
                    QueueLimit = 0
                });
            });
        });
        return services;
    }
}
```

### `Authentication.Bff.Api/Program.cs`

After `builder.Services.AddControllers();` paste:

```csharp
builder.Services.AddInsuranceRateLimiter(builder.Configuration);
```

After `app.UseAuthentication();` paste:

```csharp
app.UseRateLimiter();
```

Keep `UseAuthorization` after that.

**Check:** hammer POST `/api/claimants` → HTTP 429 JSON. GET `/health` still 200.

---

## I1 — Idempotency (SQL + BFF middleware + WASM header)

### New file `Sql/009_Idempotency.sql`

Run on local and Azure `DemoDataBase`.

```sql
USE DemoDataBase;
GO

IF OBJECT_ID('dbo.IdempotencyRecord', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IdempotencyRecord
    (
        IdempotencyKey NVARCHAR(64) NOT NULL PRIMARY KEY,
        HttpMethod NVARCHAR(10) NOT NULL,
        RequestPath NVARCHAR(256) NOT NULL,
        RequestHash NVARCHAR(64) NOT NULL,
        StatusCode INT NOT NULL,
        ResponseBody NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_IdempotencyRecord_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_IdempotencyRecord_CreatedUtc ON dbo.IdempotencyRecord (CreatedUtc);
END
GO
```

### New file `Insurance.Helpers/IdempotencyStore.cs`

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Insurance.Helpers;

public sealed class IdempotencyStore
{
    private readonly SqlCommandExecutor _sql;

    public IdempotencyStore(SqlCommandExecutor sql) => _sql = sql;

    public static string HashBody(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body ?? ""));
        return Convert.ToHexString(bytes);
    }

    public Task<IdempotencyRow?> GetAsync(string key) =>
        _sql.QuerySingleAsync<IdempotencyRow>(
            """
            SELECT IdempotencyKey, HttpMethod, RequestPath, RequestHash, StatusCode, ResponseBody
            FROM dbo.IdempotencyRecord WHERE IdempotencyKey = @key
            """,
            new { key });

    public Task SaveAsync(IdempotencyRow row) =>
        _sql.ExecuteAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.IdempotencyRecord WHERE IdempotencyKey = @IdempotencyKey)
            INSERT INTO dbo.IdempotencyRecord
                (IdempotencyKey, HttpMethod, RequestPath, RequestHash, StatusCode, ResponseBody)
            VALUES
                (@IdempotencyKey, @HttpMethod, @RequestPath, @RequestHash, @StatusCode, @ResponseBody)
            """,
            row);
}

public sealed class IdempotencyRow
{
    public string IdempotencyKey { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string RequestPath { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
}
```

### Register in `Insurance.Helpers/ServiceRegistration.cs`

Inside `AddInsuranceData` (DI order vs `SqlCommandExecutor` does not matter):

```csharp
        services.AddSingleton<IdempotencyStore>();
```

### File `Insurance.Helpers/IdempotencyMiddleware.cs` (implemented here, not under BFF)

Use `namespace Insurance.Helpers;` as in the repo. The snippet below used `Authentication.Bff.Api` in an earlier draft — **do not** create a second file.

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Insurance.Helpers;

public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IdempotencyStore store, IConfiguration config)
    {
        if (!config.GetValue("Resilience:Idempotency:Enabled", true)
            || !HttpMethods.IsPost(ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/claimants", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/claims", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/policies", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var header = config["Resilience:Idempotency:HeaderName"] ?? "Idempotency-Key";
        if (!ctx.Request.Headers.TryGetValue(header, out var keyValues))
        {
            await _next(ctx);
            return;
        }

        var key = keyValues.ToString().Trim();
        if (key.Length is 0 or > 64)
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
        var hash = IdempotencyStore.HashBody(body);

        var existing = await store.GetAsync(key);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status409Conflict;
                await ctx.Response.WriteAsJsonAsync(new { message = "Idempotency-Key was reused with a different body." });
                return;
            }

            ctx.Response.StatusCode = existing.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(existing.ResponseBody ?? "");
            return;
        }

        var original = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        await _next(ctx);
        buffer.Position = 0;
        var responseText = await new StreamReader(buffer).ReadToEndAsync();
        buffer.Position = 0;
        await buffer.CopyToAsync(original);
        ctx.Response.Body = original;

        if (ctx.Response.StatusCode is >= 200 and < 300)
        {
            await store.SaveAsync(new IdempotencyRow
            {
                IdempotencyKey = key,
                HttpMethod = ctx.Request.Method,
                RequestPath = path,
                RequestHash = hash,
                StatusCode = ctx.Response.StatusCode,
                ResponseBody = responseText
            });
        }
    }
}
```

### `Authentication.Bff.Api/Program.cs`

After `app.UseAuthorization();` paste:

```csharp
app.UseMiddleware<IdempotencyMiddleware>();
```

(Before `MapControllers`.)

### WASM — `Insurance.UI.BlazorWasm/Services/ClaimsService.cs`

Replace `CreateClaimantAsync` with:

```csharp
    public async Task<(bool Ok, string Error)> CreateClaimantAsync(CreateClaimantRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/claimants")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await _http.SendAsync(message);
        return response.IsSuccessStatusCode
            ? (true, "")
            : (false, await response.Content.ReadAsStringAsync());
    }
```

Each click gets a **new** GUID. Two clicks = two keys = two rows (correct). To test idempotency, send the **same** key twice from Postman.

**Check:** run `Sql/009`. Save claimant from UI still works. Postman: same `Idempotency-Key` + same JSON twice → one SQL claimant, second response replayed.

---

## R3 — SQL transient retry (Azure SQL pause/wake)

Replace `Insurance.Helpers/SqlCommandExecutor.cs` with:

```csharp
using Dapper;
using Microsoft.Data.SqlClient;

namespace Insurance.Helpers;

public sealed class SqlCommandExecutor
{
    private static readonly int[] Transient =
        [40613, 40197, 40501, 49918, 10928, 10929, 10053, 10054, 10060, 233, 64, 20, -2];

    private readonly string _connectionString;

    public SqlCommandExecutor(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout < 60)
            builder.ConnectTimeout = 60;
        _connectionString = builder.ConnectionString;
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            var rows = await connection.QueryAsync<T>(sql, parameters);
            return (IReadOnlyList<T>)rows.AsList();
        });

    public Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters);
        });

    public Task<int> ExecuteAsync(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.ExecuteAsync(sql, parameters);
        });

    private static async Task<T> WithRetry<T>(Func<Task<T>> action)
    {
        const int max = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (SqlException ex) when (attempt < max && Transient.Contains(ex.Number))
            {
                await Task.Delay(500 * attempt);
            }
        }
    }
}
```

**Check:** pause Azure SQL, then Save claimant — first call may wait; row still inserts (or wait for wake). Unique-key errors are **not** retried.

---

## Azure flags (after deploy)

On **pk-ins-bff** (confirmed in portal):

| App setting | Example |
| --- | --- |
| `Resilience__Retry__Enabled` | `true` |
| `Resilience__CircuitBreaker__Enabled` | `true` |
| `Resilience__RateLimit__Enabled` | `true` |
| `Resilience__Idempotency__Enabled` | `true` |

Same four on **pk-ins-claims** are optional. Only Retry/CircuitBreaker are read by Claims.Api (`ClaimantCreatedClient`). RateLimit/Idempotency on Claims are unused until that API gets the BFF middleware.

Set any to `false` to turn that piece off without a code change (**R1 handler** still requires a **publish** if you remove the pipeline; flags skip rate limit / idempotency middleware immediately).

**Publish pk-ins-bff** with this solution or Azure still runs the old binaries. Connection string **`DemoDataBase`** (SQLAzure) must stay on BFF so idempotency SQL works.

---

## Done when

| Step | Test | Local |
| --- | --- | --- |
| R1 | BFF builds; Function 500 retried; claimant still saved | Build OK |
| R2 | Gateway client has resilience (no extra Azure app) | Wired |
| L1 | Burst POST → 429; `/health` OK | Code in; runtime after publish |
| I1 | Table exists; duplicate Postman key → one claimant row | Local table exists; Azure SQL + publish still needed if not run |
| R3 | SQL wake does not fail the first login forever | Code in |
| F | Portal flags on BFF; publish | Flags present; **publish BFF** |
