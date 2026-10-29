# Azure App Service + SQL — minimum cost (OpenAI later)

Do **not** create Azure OpenAI yet. This pass is: **one SQL database + one cheap App Service plan + four web apps**.

The browser still talks only to the **BFF**. Claims / Policies / Billing stay separate APIs. SignalR stays **in-process** on BFF (`SignalR:UseAzure` false). Agent chat will 404/fail until you add OpenAI later — that is expected.

---

## What you create (and what you skip)

| Create | Why |
| --- | --- |
| Resource group `rg-insurance-dev` | Folder |
| Azure SQL **Serverless** + database `DemoDataBase` | Replaces LocalDB |
| **One** App Service plan **B1 Linux** | One bill; all apps share it |
| Web apps: `…-bff`, `…-claims`, `…-policies`, `…-billing` | APIs |
| Web app `…-ui` **or** static files inside BFF | Blazor WASM |

| Do **not** create now | Why |
| --- | --- |
| Azure OpenAI / Foundry / Machine Learning | Next phase |
| Azure SignalR | Local hub on one BFF is enough |
| Ocelot App Service | Keep `UseGateway: false`; BFF calls APIs directly |
| Extra App Service **plans** (P1v3, two B1s) | Multiplies cost |
| App Service **F1 Free** for this solution | 1 GB RAM, CPU cap, SignalR/WebSockets are unreliable for 4 .NET apps |

**Typical student/dev cost:** B1 Linux ≈ USD 12–15/month + SQL Serverless **auto-pause** ≈ USD 0 when idle, a few dollars when you demo. Pause SQL and stop the web apps when you are not showing the project.

---

## Architecture on Azure

```text
https://YOUR-ui.azurewebsites.net          Blazor WASM
https://YOUR-bff.azurewebsites.net         BFF + cookie + SignalR + (later SK)
https://YOUR-claims.azurewebsites.net      Claims.Api
https://YOUR-policies.azurewebsites.net    Policies.Api
https://YOUR-billing.azurewebsites.net     Billing.Api
        \________________ all use Azure SQL DemoDataBase
```

Cookie: HTTPS uses `__Host-AppSession`. UI and BFF are **different** `azurewebsites.net` hosts, so the login cookie is **cross-site**. You must set **SameSite=None; Secure** on the BFF cookie (paste in Step 6). CORS `UiOrigin` must be the UI HTTPS URL.

---

## Step 1 — Resource group

Portal → **Resource groups** → **Create**

- Subscription: yours  
- Name: `rg-insurance-dev`  
- Region: **East US** (or the region closest to you that supports SQL + App Service)  
- Review + create  

---

## Step 2 — Azure SQL (minimum)

### 2a. SQL server (logical server)

Portal → **SQL servers** (not “SQL virtual machine”) → **Create**

- Resource group: `rg-insurance-dev`  
- Server name: `sql-ins-<yourinitials>` (global unique)  
- Location: same as the group  
- Authentication: **Use SQL authentication**  
  - Admin login: `insadmin`  
  - Password: long password you save in a notepad (not git)  
- Allow Azure services to access this server: **Yes** (Networking tab)

### 2b. Database (Serverless)

On the server → **Create database**

- Database name: **`DemoDataBase`** (must match `USE DemoDataBase` and `ConnectionStrings:DemoDataBase`)  
- Want to use SQL elastic pool? **No**  
- Compute + storage → **Configure**  
  - Service tier: **General Purpose**  
  - Compute tier: **Serverless**  
  - Hardware: **Standard-series (Gen5)**  
  - Max vCores: **1**  
  - Min vCores: **0.5**  
  - **Enable auto-pause** → delay **1 hour** (or 15 minutes if offered)  
  - Data max size: **2 GB** (enough for this seed)  
- Backup: defaults  
- Review + create  

### 2c. Firewall for your PC

SQL server → **Networking** → **Add your client IPv4 address**.  
Keep **Allow Azure services** ON.

### 2d. Run the project scripts

Portal → database `DemoDataBase` → **Query editor** (login `insadmin`)  
**or** SSMS / Azure Data Studio: server `sql-ins-….database.windows.net`, SQL auth.

Run in order, **while connected to `DemoDataBase`** (if `USE DemoDataBase` errors, delete that line and run the rest):

1. `Sql\001_InsurancePlatform.sql`  
2. `Sql\002_SeedUsers.sql`  
3. `Sql\003_InsuranceClaimants.sql`  
4. `Sql\004_NotificationFlags.sql`  

Confirm: `SELECT COUNT(*) FROM dbo.Users;` is not 0.

Connection string (put in App Settings, **not** git):

```text
Server=tcp:sql-ins-YOUR.database.windows.net,1433;Initial Catalog=DemoDataBase;Persist Security Info=False;User ID=insadmin;Password=YOUR_PASSWORD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

---

## Step 3 — One App Service plan (the only compute bill)

Portal → **App Service plans** → **Create**

- Resource group: `rg-insurance-dev`  
- Name: `plan-ins-dev`  
- Operating System: **Linux**  
- Region: same as SQL  
- Pricing: **Dev/Test** → **B1** (Basic)  
- Review + create  

You will **reuse this plan** for every web app below. Do not click “create new plan” again.

---

## Step 4 — Four API web apps (same plan)

Create **Web App** four times. Each time: **same** resource group, **same** `plan-ins-dev`, **Linux**, runtime **.NET 10** (or newest .NET if 10 is not listed — then set it after publish).

Pick a unique prefix, example `pk`:

| Name | Runtime |
| --- | --- |
| `pk-ins-bff` | .NET 10 |
| `pk-ins-claims` | .NET 10 |
| `pk-ins-policies` | .NET 10 |
| `pk-ins-billing` | .NET 10 |

On **each** app → **Configuration** / **Environment variables** (Application settings):

**All four APIs:**

| Name | Value |
| --- | --- |
| `ConnectionStrings__DemoDataBase` | Azure SQL string from Step 2d |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Jwt__Issuer` | `InsurancePlatform` |
| `Jwt__Audience` | `InsuranceApis` |
| `Jwt__SigningKey` | same as local (or a new 32+ char secret, **same on all four**) |

**BFF only (`pk-ins-bff`):**

| Name | Value |
| --- | --- |
| `UiOrigin` | `https://pk-ins-ui.azurewebsites.net` (match UI URL in Step 5) |
| `UseGateway` | `false` |
| `Downstream__Claims` | `https://pk-ins-claims.azurewebsites.net` |
| `Downstream__Policies` | `https://pk-ins-policies.azurewebsites.net` |
| `Downstream__Billing` | `https://pk-ins-billing.azurewebsites.net` |
| `SignalR__Enabled` | `true` |
| `SignalR__UseAzure` | `false` |
| `Agent__UseSemanticKernel` | `false` |
| `Agent__UsePythonAgents` | `false` |

**Each API** → **Configuration** → **General** → **Web sockets: On** (needed on BFF for SignalR).

**BFF** → **CORS** in Azure portal can stay empty; the app already uses `UiOrigin`.

---

## Step 5 — UI web app (WASM)

Create one more Web App on **the same B1 plan**:

- Name: `pk-ins-ui`  
- Runtime: **.NET 10** (or **Static site** / Node if you only upload `wwwroot`)

Before you publish, set `Insurance.UI.BlazorWasm\wwwroot\appsettings.json` **or** add `wwwroot\appsettings.Production.json` that Blazor loads (WASM uses `appsettings.json` from the site root):

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

Use **your** real BFF hostname. Keep Agent **false** until OpenAI.

---

## Step 6 — Cookie for two Azure hostnames (required)

**File:** `Authentication.Bff.Api\Controllers\AuthController.cs` — `CookieOptions()`:

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

`SameSite=None` requires `Secure=true` (true on App Service HTTPS). Local HTTP stays Lax.

Stop debugging, rebuild BFF, then publish.

---

## Step 7 — Publish from Visual Studio or CLI

### Visual Studio

For each project: right-click → **Publish** → **Azure** → **Azure App Service (Linux)** → pick the matching web app → **Publish**.

Order: **Claims, Policies, Billing, BFF, UI**.

### CLI (from solution folder)

Install [Azure CLI](https://aka.ms/installazurecliwindows) → `az login`.

```bat
cd E:\pvkumar_git\insuranceplateform

dotnet publish Claims.Api\Claims.Api.csproj -c Release -o publish\claims
dotnet publish Policies.Api\Policies.Api.csproj -c Release -o publish\policies
dotnet publish Billing.Api\Billing.Api.csproj -c Release -o publish\billing
dotnet publish Authentication.Bff.Api\Authentication.Bff.Api.csproj -c Release -o publish\bff
dotnet publish Insurance.UI.BlazorWasm\Insurance.UI.BlazorWasm.csproj -c Release -o publish\ui

az webapp deploy --resource-group rg-insurance-dev --name pk-ins-claims --src-path publish\claims --type zip
```

Zip each output folder first, or use Visual Studio publish. If `az webapp deploy --type zip` needs a zip:

```bat
powershell Compress-Archive -Path publish\claims\* -DestinationPath publish\claims.zip -Force
az webapp deploy -g rg-insurance-dev -n pk-ins-claims --src-path publish\claims.zip --type zip
```

Repeat for policies, billing, bff, ui.

Linux App Service start command if the app does not boot:  
Configuration → **Startup Command**:

```text
dotnet Claims.Api.dll
```

(use `Authentication.Bff.Api.dll`, `Policies.Api.dll`, `Billing.Api.dll` on the others). WASM UI uses the default static + `wwwroot` from the publish folder (`wwwroot` inside the Blazor publish output).

---

## Step 8 — Smoke test (no OpenAI)

1. Open `https://pk-ins-ui.azurewebsites.net`  
2. Login with the **seed user** from `002_SeedUsers.sql`  
3. Claims list, add claimant, add claim, bell/notifications  

If login 401: cookie SameSite, `UiOrigin` mismatch, or SQL firewall.  
If 502 on claims: `Downstream__Claims` URL wrong or Claims app not started.  
If SQL timeout on first hit: Serverless is **resuming** (wait 30–60 s, retry).

---

## Stop spending money when idle

- SQL: auto-pause already on.  
- App Service plan: **Stop** each web app (plan still bills B1 until you **Delete** the plan or scale to **Free F1**).  
- To go to **zero compute**: stop/delete web apps and **delete** `plan-ins-dev`. Keep SQL paused.

---

## After this works — Azure OpenAI (next)

1. Create **Azure OpenAI** in `rg-insurance-dev`.  
2. Deploy `gpt-4o-mini`.  
3. BFF App Settings (not git):

| Name | Value |
| --- | --- |
| `Agent__UseSemanticKernel` | `true` |
| `Agent__AzureOpenAI__Endpoint` | `https://….openai.azure.com/` |
| `Agent__AzureOpenAI__ApiKey` | key |
| `Agent__AzureOpenAI__Deployment` | `gpt-4o-mini` |

4. UI `Agent:Enabled` true, republish UI.

---

## Checklist

- [ ] Resource group  
- [ ] SQL server + serverless `DemoDataBase` + firewall  
- [ ] Scripts 001–004  
- [ ] One B1 Linux plan  
- [ ] Four API apps + UI app on that plan  
- [ ] Connection string + Downstream + UiOrigin  
- [ ] Web sockets on BFF  
- [ ] SameSite=None cookie paste  
- [ ] Publish five projects  
- [ ] Login + claims on HTTPS  
- [ ] OpenAI **not** created yet  
