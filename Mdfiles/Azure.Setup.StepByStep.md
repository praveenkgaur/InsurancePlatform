# Azure setup — your environment (pk)

Budget: **`pk-insurance-learn-12`** (USD 12 / month).  
Resource group: **`pk-insurance-dev`**.  
Subscription: **Azure subscription 1**.  
Region: **South India**.

Do **not** use `*.azurewebsites.net` **without** the unique stamp. Each Web App Default domain is:

`https://<app-name>-<stamp>.southindia-01.azurewebsites.net`

Wrong (DNS NXDOMAIN): `https://pk-ins-claims.azurewebsites.net`  
Wrong: reuse another app’s stamp (for example the old UI stamp `a7aebdgfsdecmah` on claims).  
Right: that app’s Overview → **Default domain**.

The BFF does **not** host Blazor. **pk-ins-ui** is the website. **pk-ins-bff** is cookie auth + proxy + SignalR. Claims / policies / billing are separate APIs. Ocelot is **not** deployed (`UseGateway: false`).

---

## Your resources (done)

| Resource | Your value |
| --- | --- |
| Budget | `pk-insurance-learn-12` |
| Resource group | `pk-insurance-dev` |
| SQL logical server | `pk-ins-demodb` |
| SQL admin | `insadmin` (SQL auth, not Entra) |
| SQL host | `pk-ins-demodb.database.windows.net` (**not** `demoddb`) |
| SQL database | `DemoDataBase` (Serverless) |
| App Service plan | `plan-ins-dev` **Windows F1 Free** |
| Web apps | `pk-ins-bff`, `pk-ins-claims`, `pk-ins-policies`, `pk-ins-billing`, `pk-ins-ui` |

### Default domains (live — 29 Oct 2026)

Copied from working `/health/downstream` and BFF Overview. If a stamp changes, use Overview again.

| App name | Default domain |
| --- | --- |
| **pk-ins-bff** | `https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net` |
| **pk-ins-claims** | `https://pk-ins-claims-gth7cvg6etb8hqa0.southindia-01.azurewebsites.net` |
| **pk-ins-policies** | `https://pk-ins-policies-cqexgrdtgwbgc9cr.southindia-01.azurewebsites.net` |
| **pk-ins-billing** | `https://pk-ins-billing-g5ecgqcmhmfaf7dr.southindia-01.azurewebsites.net` |
| **pk-ins-ui** | Overview of **pk-ins-ui** → copy **Default domain**. The old value `https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net` is **dead** (NXDOMAIN). |

Stamps are **all different**: BFF `c7d5a5fzfsh7gzch`, claims `gth7cvg6etb8hqa0`, policies `cqexgrdtgwbgc9cr`, billing `g5ecgqcmhmfaf7dr`.

SQL connection string (password only in Azure / local note, **not** git):

```text
Server=tcp:pk-ins-demodb.database.windows.net,1433;Initial Catalog=DemoDataBase;User ID=insadmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
```

Demo login (from `Sql/002_SeedUsers.sql`): **`Praveen` / `Password@123`**. Adjuster: **`Adjuster` / `Password@123`**.

---

## Where each setting goes

Azure **Environment variables** override published `appsettings.Production.json`.

### A — All four APIs (`pk-ins-bff`, `pk-ins-claims`, `pk-ins-policies`, `pk-ins-billing`)

**Environment variables → Connection strings**

| Name | Type | Value |
| --- | --- | --- |
| `DemoDataBase` | **SQLAzure** | SQL string above |

JWT: optional. If you set `Jwt__SigningKey`, use the **same** value on all four.

**Do not** add `UiOrigin` / `Downstream__*` on claims, policies, or billing.

SQL server **`pk-ins-demodb`** → Networking: **Selected networks**, your client IP, **Allow Azure services**, Save. Run `Sql/001`–`004` on `DemoDataBase` if tables are missing.

### B — BFF only (`pk-ins-bff`)

**Environment variables → App settings** (not Connection strings, except SQL):

| Name | Your value |
| --- | --- |
| `UiOrigin` | **pk-ins-ui** Default domain from Overview (no trailing `/`). Must match the browser origin exactly. |
| `UseGateway` | `false` |
| `Downstream__Claims` | `https://pk-ins-claims-gth7cvg6etb8hqa0.southindia-01.azurewebsites.net` |
| `Downstream__Policies` | `https://pk-ins-policies-cqexgrdtgwbgc9cr.southindia-01.azurewebsites.net` |
| `Downstream__Billing` | `https://pk-ins-billing-g5ecgqcmhmfaf7dr.southindia-01.azurewebsites.net` |
| `SignalR__Enabled` | `true` |
| `SignalR__UseAzure` | `false` |
| `Agent__UseSemanticKernel` | `false` |
| `Agent__UsePythonAgents` | `false` |

**Apply** → **Save**.

**pk-ins-bff → CORS** (API blade, not Environment variables): **Allowed Origins empty**. Delete `*`. Credentials **off**. The app uses `UiOrigin` in code. Azure `*` breaks cookies.

**Configuration → General settings** on BFF:

- **Web sockets:** On  
- **SCM Basic Auth Publishing Credentials:** On (needed to download publish profile)  
- F1 stays **32 Bit**; **Always On** stays off  

### C — UI (`pk-ins-ui`)

**No** Azure App settings for BFF URL. WASM only reads published `wwwroot` json.

| File | When | Value |
| --- | --- | --- |
| `wwwroot/appsettings.json` | local F5 | `http://localhost:5281` |
| `wwwroot/appsettings.Development.json` | local F5 | same localhost |
| `wwwroot/appsettings.Production.json` | Azure publish | BFF Default domain below |

```json
{
  "BffBaseUrl": "https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net",
  "SignalR": {
    "Enabled": true,
    "UseAzure": false,
    "HubUrl": "https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net/hubs/notifications",
    "ClientMethodName": "ReceiveNotification"
  },
  "Agent": {
    "Enabled": false
  }
}
```

---

## Cookie and CORS (UI and BFF are two hosts)

HTTPS cookie: `__Host-AppSession`, `HttpOnly`, `Secure`, `SameSite=None` when the page origin is not the BFF host.

Login URL: **UI** Default domain + `/login`  
API calls: **BFF** + `/auth/login`, `/auth/me`, `/api/claims`, …  
BFF `/` returns **404** — that is normal (no Blazor on the BFF).

`GET /auth/me` **401** on the login page (not signed in yet) is normal. After Sign in, `/auth/login` and `/api/claims` should be **200**.

Use a **normal** Chrome window. Incognito often blocks the third-party BFF cookie.

---

## GitHub CD

Repo: `praveenkgaur/InsurancePlatform`  
Workflow: `.github/workflows/deploy-azure.yml`  
Branch: **`SP01.01.00.00`** (also `main`).

Push publishes **five separate zips** (BFF, claims, policies, billing, UI). UI `wwwroot` is **not** copied into the BFF.

**Repository secrets** (Settings → Secrets and variables → Actions):

| Secret | Azure app |
| --- | --- |
| `AZURE_PUBLISH_PROFILE_BFF` | `pk-ins-bff` |
| `AZURE_PUBLISH_PROFILE_CLAIMS` | `pk-ins-claims` |
| `AZURE_PUBLISH_PROFILE_POLICIES` | `pk-ins-policies` |
| `AZURE_PUBLISH_PROFILE_BILLING` | `pk-ins-billing` |
| `AZURE_PUBLISH_PROFILE_UI` | `pk-ins-ui` |

Download: each Web App → Overview → **Download publish profile** (enable **SCM Basic Auth** first).

---

## Health and Swagger (after deploy)

| Check | URL |
| --- | --- |
| BFF health | `https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net/health` |
| BFF → APIs | `https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net/health/downstream` |
| BFF Swagger | `https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net/swagger` |
| Claims Swagger | `https://pk-ins-claims-gth7cvg6etb8hqa0.southindia-01.azurewebsites.net/swagger` |
| Policies Swagger | `https://pk-ins-policies-cqexgrdtgwbgc9cr.southindia-01.azurewebsites.net/swagger` |
| Billing Swagger | `https://pk-ins-billing-g5ecgqcmhmfaf7dr.southindia-01.azurewebsites.net/swagger` |

`/health` needs **no token**. `"ok": true` on all three downstream rows means the 502 hostnames are correct. Claims **data** in Swagger still needs JWT (the BFF sends that; the browser does not).

---

## Recreate from scratch (if you ever delete the group)

1. Budget `pk-insurance-learn-12` (already exists).  
2. Resource group `pk-insurance-dev`.  
3. **SQL logical servers** → Create `pk-ins-demodb`, SQL auth `insadmin`, South India (East US did not allow SQL).  
4. Networking: **Selected networks**, client IP, **Allow Azure services**, Save.  
5. **SQL databases** → Create `DemoDataBase` Serverless on **existing** server (not VM, not Managed Instance).  
6. Run `Sql/001_InsurancePlatform.sql` → `002_SeedUsers.sql` → `003` → `004`.  
7. **App Service plans** → `plan-ins-dev`, Windows, **F1**, South India.  
8. **App Services** → **Web App** five times, existing plan, **.NET 10**, names above.  
9. Settings as in **Where each setting goes** (use **new** Default domains from Overview).  
10. Push `SP01.01.00.00` → **Deploy Azure App Service**.

---

## Test

1. Open **pk-ins-ui** Default domain + `/login` (not the BFF URL, not the dead `a7aebdgfsdecmah` host).  
2. Network: `/auth/me` goes to **`pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net`**.  
3. Sign in `Praveen` / `Password@123`.  
4. Claims list: `GET …/api/claims` **200**.  
5. First SQL call after pause may take 30–60 s.

| Error | Cause |
| --- | --- |
| `ERR_NAME_NOT_RESOLVED` on `pk-ins-*.azurewebsites.net` (no stamp) | Short hostname — use Default domain |
| `No such host` on `/health/downstream` | Downstream reused another app’s stamp |
| `GET /api/claims` **502** | `Downstream__Claims` wrong or claims app down |
| BFF `/` HTTP 404 | Normal |
| Login CORS / missing ACAO | Azure CORS has `*`, or `UiOrigin` ≠ UI Default domain |
| `POST /auth/login` **500** ~15 s | SQL connection string, firewall, or scripts not run |
| Login fails only in Incognito | Third-party cookie (UI host ≠ BFF host) — use a normal window |

---

## Later — Azure OpenAI

BFF App settings: `Agent__UseSemanticKernel=true` plus `Agent__AzureOpenAI__Endpoint`, `ApiKey`, `Deployment`.  
UI Production: `"Agent": { "Enabled": true }`, then deploy.  
See `Mdfiles/SemanticKernel.Agent.PasteGuide.md`.

---

## Checklist

- [x] Budget, RG, SQL server + firewall, F1 plan, five web apps  
- [x] GitHub publish-profile secrets  
- [x] CD on `main` and `SP01.01.00.00`  
- [x] `Downstream__*` = long claims / policies / billing Default domains (`ok: true` on `/health/downstream`)  
- [x] Demo SQL login works (`Praveen` / `Password@123`)  
- [ ] `UiOrigin` + browser login = **current** pk-ins-ui Default domain (not `a7aebdgfsdecmah`)  
- [ ] Web sockets On (BFF)  
- [ ] Azure BFF CORS list empty (no `*`)  
- [ ] OpenAI later  
