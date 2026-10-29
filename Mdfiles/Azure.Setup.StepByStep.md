# Azure setup — your environment (pk)

Budget: **`pk-insurance-learn-12`** (USD 12 / month).  
Resource group: **`pk-insurance-dev`**.  
Subscription: **Azure subscription 1**.

Do **not** use `*.azurewebsites.net` **without** the unique stamp. New App Services use:

`https://<app-name>-<stamp>.southindia-01.azurewebsites.net`

Wrong (DNS NXDOMAIN): `https://pk-ins-ui.azurewebsites.net`  
Right: Overview → **Default domain** on each app.

---

## Your resources (done)

| Resource | Your value |
| --- | --- |
| Budget | `pk-insurance-learn-12` |
| Resource group | `pk-insurance-dev` |
| SQL logical server | `pk-ins-demodb` |
| SQL region | **South India** / India South Central |
| SQL admin | `insadmin` (SQL auth, not Entra) |
| SQL host | `pk-ins-demodb.database.windows.net` (**not** `demoddb`) |
| App Service plan | `plan-ins-dev` **Windows F1 Free** |
| Web apps (names) | `pk-ins-bff`, `pk-ins-claims`, `pk-ins-policies`, `pk-ins-billing`, `pk-ins-ui` |

### Default domains (copy from Overview if a stamp changes)

| App name | Default domain (this project) |
| --- | --- |
| **pk-ins-ui** | `https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net` |
| **pk-ins-bff** | `https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net` |
| **pk-ins-claims** | Overview of **pk-ins-claims** → `https://pk-ins-claims-<stamp>.southindia-01.azurewebsites.net` |
| **pk-ins-policies** | Overview of **pk-ins-policies** → same pattern |
| **pk-ins-billing** | Overview of **pk-ins-billing** → same pattern |

Each stamp is **different**. Do not reuse the UI stamp `a7aebdgfsdecmah` on claims.

SQL connection string (password only in Azure / local note, not git):

```text
Server=tcp:pk-ins-demodb.database.windows.net,1433;Initial Catalog=DemoDataBase;User ID=insadmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

---

## Where each setting goes

### A — All four APIs (`pk-ins-bff`, `pk-ins-claims`, `pk-ins-policies`, `pk-ins-billing`)

**Environment variables → Connection strings**

| Name | Type | Value |
| --- | --- | --- |
| `DemoDataBase` | **SQLAzure** | SQL string above |

JWT: optional in Azure. If omitted, published `appsettings.json` is used. If you set `Jwt__SigningKey`, use the **same** value on all four.

**Do not** add `UiOrigin` / `Downstream__*` on claims, policies, or billing.

### B — BFF only (`pk-ins-bff`)

**Environment variables → App settings** (not Connection strings, except SQL):

| Name | Example value (yours) |
| --- | --- |
| `UiOrigin` | `https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net` |
| `UseGateway` | `false` |
| `Downstream__Claims` | Default domain of **pk-ins-claims** (no trailing `/`) |
| `Downstream__Policies` | Default domain of **pk-ins-policies** |
| `Downstream__Billing` | Default domain of **pk-ins-billing** |
| `SignalR__Enabled` | `true` |
| `SignalR__UseAzure` | `false` |
| `Agent__UseSemanticKernel` | `false` |
| `Agent__UsePythonAgents` | `false` |

Then **Apply** → **Save**.

**Configuration → General settings** on BFF:

- **Web sockets:** On  
- **SCM Basic Auth Publishing Credentials:** On (needed to download publish profile)  
- F1 stays **32 Bit**; **Always On** stays off  

### C — UI (`pk-ins-ui`)

**No** Azure App settings for BFF URL. WASM only reads `wwwroot` json.

| File | When | Example |
| --- | --- | --- |
| `wwwroot/appsettings.json` | local F5 | `http://localhost:5281` |
| `wwwroot/appsettings.Development.json` | local F5 | same localhost |
| `wwwroot/appsettings.Production.json` | Azure publish | BFF Default domain below |

**Production UI json (this project):**

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

**BFF `appsettings.Production.json`:** `UiOrigin` = UI Default domain; `Downstream` = the three API Default domains. Azure App settings **override** this file.

---

## Cookie (local + Azure)

`AuthController.CookieOptions`: HTTPS → `SameSite=None`; HTTP localhost → `Lax`.

Login URL: **UI** Default domain + `/login`  
API calls: **BFF** Default domain + `/auth/me`  
BFF `/` returns **404** (API has no home page) — that is normal.

---

## GitHub CD

Repo: `praveenkgaur/InsurancePlatform`  
Workflow: `.github/workflows/deploy-azure.yml`

**Triggers:** push to **`main`** or **`SP01.01.00.00`**, or Actions → **Run workflow**.

**Repository secrets** (Settings → Secrets and variables → Actions), not Deploy keys, not Environment secrets:

| Secret | Azure app |
| --- | --- |
| `AZURE_PUBLISH_PROFILE_BFF` | `pk-ins-bff` |
| `AZURE_PUBLISH_PROFILE_CLAIMS` | `pk-ins-claims` |
| `AZURE_PUBLISH_PROFILE_POLICIES` | `pk-ins-policies` |
| `AZURE_PUBLISH_PROFILE_BILLING` | `pk-ins-billing` |
| `AZURE_PUBLISH_PROFILE_UI` | `pk-ins-ui` |

Download profile: each Web App → Overview → **Download publish profile** (enable **SCM Basic Auth** first).

Deploys **app name** `pk-ins-ui` (correct). Browser URL is still the **Default domain**, not `pk-ins-ui.azurewebsites.net`.

Ocelot is **not** deployed (`UseGateway: false`).

---

## Recreate from scratch (if you ever delete the group)

1. Budget `pk-insurance-learn-12` (already exists).  
2. Resource group `pk-insurance-dev`.  
3. **SQL logical servers** → Create `pk-ins-demodb`, SQL auth `insadmin`, region that allows SQL (South India worked; East US did not).  
4. Networking: **Selected networks**, client IP, **Allow Azure services**, Save.  
5. **SQL databases** → Create `DemoDataBase` Serverless on **existing** server (not VM, not Managed Instance).  
6. Run `Sql\001`–`004` (Query editor or SSMS: server `pk-ins-demodb.database.windows.net`, SQL login).  
7. **App Service plans** → `plan-ins-dev`, Windows, **F1**, South India.  
8. **App Services** → **Web App** five times, existing plan, **.NET 10**, names above.  
9. Settings as in **Where each setting goes**.  
10. Push branch → **Deploy Azure App Service**.

---

## Test

1. Open **`https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net/login`**.  
2. DevTools Network: `/auth/me` must go to **`pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net`**, not `pk-ins-bff.azurewebsites.net`.  
3. Sign in with seed user from `002_SeedUsers.sql`.  
4. First SQL call may take 30–60 s (Serverless resume).

| Error | Cause |
| --- | --- |
| `ERR_NAME_NOT_RESOLVED` on `pk-ins-bff.azurewebsites.net` | UI Production json / cache still has short host — push json, Ctrl+F5 |
| BFF `/` HTTP 404 | Normal |
| Login CORS / cookie | `UiOrigin` must equal the UI Default domain exactly |
| 40615 / SQL timeout | Firewall IP changed, or serverless waking |

---

## Later — Azure OpenAI

BFF App settings: `Agent__UseSemanticKernel=true` plus `Agent__AzureOpenAI__Endpoint`, `ApiKey`, `Deployment`.  
UI Production: `"Agent": { "Enabled": true }`, then deploy.  
See `Mdfiles\SemanticKernel.Agent.PasteGuide.md`.

---

## Checklist

- [x] Budget, RG, SQL server + firewall, F1 plan, five web apps  
- [x] GitHub publish-profile secrets  
- [x] CD on `main` and `SP01.01.00.00`  
- [ ] `DemoDataBase` + scripts 001–004 (if not run yet)  
- [ ] BFF App settings = **long** Default domains (not `*.azurewebsites.net` short names)  
- [ ] UI Production json pushed; login `/auth/me` hits long BFF host  
- [ ] Web sockets On (BFF)  
- [ ] OpenAI later  
