# Azure setup — step by step (learning, ~USD 10–12)

Budget **already created:** `pk-insurance-learn-12` (USD 12 / month, Cost Management).  
Do **not** create Azure OpenAI, AKS, APIM Developer, or extra App Service plans until this URL login works.

Keep **F1 Free** for App Service. **B1 (~USD 12–15)** will use almost the whole budget by itself.

---

## Done

- [x] Budget `pk-insurance-learn-12`
- [x] Resource group `pk-insurance-dev`
- [x] SQL logical server `pk-ins-demodb` (**South India** / India South Central)
- [x] SQL firewall: Selected networks + client IP + Allow Azure services

**SQL host (no extra `d`):** `pk-ins-demodb.database.windows.net`  
(Wrong: `pk-ins-demoddb` → DNS error 11001)

---

---

## Step 1 — Resource group

1. Portal → **Resource groups** → **Create**.
2. Subscription: the same one as the budget.
3. Name: `rg-insurance-dev`.
4. Region: **East US** (use this region for SQL and App Service too).
5. **Review + create** → **Create**.

---

## Step 2 — SQL server (logical server)

1. Search **SQL servers** → **Create** (not SQL virtual machine).
2. Resource group: `rg-insurance-dev`.
3. Server name: `sql-ins-<yourinitials>` (must be globally unique).
4. Location: **East US**.
5. Authentication: **Use SQL authentication**.
   - Login: `insadmin`
   - Password: strong password (save in a local note, **not** git).
6. Networking: **Allow Azure services and resources to access this server** = **Yes**.
7. **Review + create** → **Create**. Wait until it finishes.

### Firewall for your PC

1. Open the SQL **server** (not the database yet).
2. **Networking**.
3. **Add your client IPv4 address**.
4. Save.

---

## Step 3 — Database `DemoDataBase` (Serverless)

1. On the SQL server → **Create database**.
2. Database name: **`DemoDataBase`** (exact spelling).
3. Elastic pool: **No**.
4. Compute + storage → **Configure**:
   - Service tier: **General Purpose**
   - Compute: **Serverless**
   - Hardware: Gen5
   - Max vCores: **1**
   - Min vCores: **0.5**
   - **Auto-pause**: **60 minutes** (or 15 if shown)
   - Max data size: **2 GB**
5. **Review + create** → **Create**.

---

## Step 4 — Run SQL scripts

1. Open database `DemoDataBase` → **Query editor** → sign in as `insadmin`.
2. If `USE DemoDataBase;` errors, delete that line; you are already in this database.
3. Run in order (from the repo `Sql` folder):
   1. `001_InsurancePlatform.sql`
   2. `002_SeedUsers.sql`
   3. `003_InsuranceClaimants.sql`
   4. `004_NotificationFlags.sql`
4. Check: `SELECT COUNT(*) FROM dbo.Users;` — should be &gt; 0.

Connection string (App Settings later; password secret):

```text
Server=tcp:pk-ins-demodb.database.windows.net,1433;Initial Catalog=DemoDataBase;User ID=insadmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

---

## Step 5 — App Service plan (F1 Free)

SQL is in **South India**. Put the plan in **South India** too (East US still works, but slower).

**Which portal tile:** All services → search `app` → click **App Service plans** (not **App Services** yet, not **AI Search / Foundry**).

1. **App Service plans** → **Create**.
2. Subscription: **Azure subscription 1**.
3. Resource group: **`pk-insurance-dev`** (the one you already have; not `rg-insurance-dev`).
4. Name: `plan-ins-dev`.
5. Operating system: **Windows** (Linux often has **no F1**).
6. Region: **South India** (same as `pk-ins-demodb`). If F1 is missing in South India, try **East US** / **East US 2** on **Windows** — apps can still reach SQL.
7. Pricing tab: **Dev/Test** → **F1** (Free).  
   Size should show **F1** / **Shared infrastructure** / **1 GB**. Do **not** pick **B1**, **B2**, or **P1v3**.
8. **Review + create** → **Create**. Wait until Succeeded.

If the portal only offers **B1** and no F1: change OS back to **Windows** and Dev/Test. B1 (~USD 12–15) will consume the `pk-insurance-learn-12` budget by itself.

**Next (Step 6):** All services → **App Services** (blue cloud) → **Create** web apps on **existing** plan `plan-ins-dev`. Do not create a second plan.

---

## Step 6 — Five web apps (same plan)

Create **Web App** five times. Every time: **existing plan** `plan-ins-dev`. Never “create new plan”.

Replace `pk` if the name is taken:

| Web app name | What it hosts |
| --- | --- |
| `pk-ins-bff` | Authentication.Bff.Api |
| `pk-ins-claims` | Claims.Api |
| `pk-ins-policies` | Policies.Api |
| `pk-ins-billing` | Billing.Api |
| `pk-ins-ui` | Insurance.UI.BlazorWasm |

Runtime: **.NET 10** (or newest .NET listed).

On **`pk-ins-bff`** → Configuration → **General settings** → **Web sockets: On** → Save.

---

## Step 7 — Application settings

**Configuration** → **Application settings** → **+ New application setting**.

### All four APIs (`bff`, `claims`, `policies`, `billing`)

| Name | Value |
| --- | --- |
| `ConnectionStrings__DemoDataBase` | SQL string from Step 4 |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Jwt__Issuer` | `InsurancePlatform` |
| `Jwt__Audience` | `InsuranceApis` |
| `Jwt__SigningKey` | one 32+ character secret, **same on all four** |

### BFF only (`pk-ins-bff`)

| Name | Value |
| --- | --- |
| `UiOrigin` | `https://pk-ins-ui.azurewebsites.net` |
| `UseGateway` | `false` |
| `Downstream__Claims` | `https://pk-ins-claims.azurewebsites.net` |
| `Downstream__Policies` | `https://pk-ins-policies.azurewebsites.net` |
| `Downstream__Billing` | `https://pk-ins-billing.azurewebsites.net` |
| `SignalR__Enabled` | `true` |
| `SignalR__UseAzure` | `false` |
| `Agent__UseSemanticKernel` | `false` |
| `Agent__UsePythonAgents` | `false` |

Save → the app will restart.

---

## Step 8 — UI appsettings (local + Azure)

**Do not** put Azure URLs in `wwwroot\appsettings.json`. That file stays **localhost** so F5 still works.

Blazor WASM loads `appsettings.json` then `appsettings.{Environment}.json`:

| File | When | BFF URL |
| --- | --- | --- |
| `wwwroot\appsettings.json` | both | `http://localhost:5281` |
| `wwwroot\appsettings.Development.json` | Visual Studio F5 | localhost (same) |
| `wwwroot\appsettings.Production.json` | publish to `pk-ins-ui` | `https://pk-ins-bff.azurewebsites.net` |

`appsettings.Production.json` (already in the repo):

```json
{
  "BffBaseUrl": "https://pk-ins-bff.azurewebsites.net",
  "SignalR": {
    "Enabled": true,
    "UseAzure": false,
    "HubUrl": "https://pk-ins-bff.azurewebsites.net/hubs/notifications",
    "ClientMethodName": "ReceiveNotification"
  },
  "Agent": {
    "Enabled": false
  }
}
```

If your UI hostname is not `pk-ins-ui` / BFF is not `pk-ins-bff`, edit **Production** only. BFF Azure App settings still override BFF json; the **UI cannot** read those — it only reads these wwwroot files.

Do **not** put the SQL password in any UI json.

---

## Step 9 — Cookie for two Azure hostnames

**File:** `Authentication.Bff.Api\Controllers\AuthController.cs` — method `CookieOptions()`:

```csharp
private CookieOptions CookieOptions() => new()
{
    HttpOnly = true,
    Secure = Request.IsHttps,
    SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
    Path = "/",
    Expires = DateTimeOffset.UtcNow.AddHours(8)
};
```

Shift+F5 (stop debug) → rebuild BFF. HTTPS cookie is `__Host-AppSession` with **SameSite=None**.

---

## Step 10 — Publish

Visual Studio, each project → **Publish** → **Azure App Service** → matching web app:

1. Claims.Api → `pk-ins-claims`  
2. Policies.Api → `pk-ins-policies`  
3. Billing.Api → `pk-ins-billing`  
4. Authentication.Bff.Api → `pk-ins-bff`  
5. Insurance.UI.BlazorWasm → `pk-ins-ui`  

---

## Step 11 — Test (no OpenAI)

1. Browser: `https://pk-ins-ui.azurewebsites.net`  
2. Login with the seed user from `002_SeedUsers.sql`.  
3. Open Claims / Claimants. First SQL hit may wait **30–60 s** (Serverless resume).

| Symptom | Check |
| --- | --- |
| Login 401 / cookie | Step 9 SameSite, `UiOrigin` exact UI URL |
| 502 on claimants | `Downstream__Claims`, Claims app **Running** |
| SQL timeout | firewall + wait for serverless resume |
| Site won’t start | F1 1 GB RAM — stop unused apps; try one API first |

---

## Step 12 — Azure OpenAI (only after login works)

1. Confirm Cost analysis is still under the budget.  
2. Create **Azure OpenAI** in `pk-insurance-dev`.  
3. Deploy model `gpt-4o-mini`.  
4. BFF settings: `Agent__UseSemanticKernel=true`, `Agent__AzureOpenAI__Endpoint`, `ApiKey`, `Deployment`.  
5. UI `Agent:Enabled` true, republish UI.

Details: `Mdfiles\SemanticKernel.Agent.PasteGuide.md`.

---

## Do not create yet

- Extra App Service **plans**
- Azure SignalR, APIM, AKS, Machine Learning
- Linux **B1** (unless you drop F1 and accept the budget alert)

---

## Checklist

- [x] Budget `pk-insurance-learn-12`
- [x] `pk-insurance-dev`
- [x] SQL server `pk-ins-demodb` + firewall
- [ ] `DemoDataBase` Serverless + scripts 001–004
- [ ] Plan `plan-ins-dev` **F1**
- [ ] Five web apps on that plan
- [ ] App settings + UI appsettings
- [ ] SameSite=None cookie
- [ ] Publish five projects
- [ ] Login on HTTPS UI
- [ ] OpenAI later
