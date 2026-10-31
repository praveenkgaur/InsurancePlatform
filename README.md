# Insurance Platform (BFF cookie + Ocelot)

How the UI, APIs, cookies, JWT, and HttpClientFactory run is in [Mdfiles/InsurancePlatform.HowItWorks.md](Mdfiles/InsurancePlatform.HowItWorks.md).

Blazor WebAssembly talks only to `Authentication.Bff.Api`. Login sets an HttpOnly cookie (`AppSession` on HTTP, `__Host-AppSession` on HTTPS). The BFF stores the JWT and sends `Authorization: Bearer` to Claims, Policies, and Billing (`UseGateway: false` on Azure; Ocelot is optional locally).

## Demo users

- `Praveen` / `Password@123` — Admin (view, approve, delete)
- `Adjuster` / `Password@123` — ClaimsAdjuster (view and approve only)

## Local URLs

SQL scripts are already applied to LocalDB `DemoDataBase`.

```
dotnet run --project Billing.Api
dotnet run --project Policies.Api
dotnet run --project Claims.Api
dotnet run --project Authentication.Bff.Api
dotnet run --project Insurance.UI.BlazorWasm
```

| What | URL |
|---|---|
| UI (open this) | http://localhost:5280/login |
| BFF (UI calls only this) | http://localhost:5281 |
| Claims | http://localhost:5283 |
| Policies | http://localhost:5284 |
| Billing | http://localhost:5285 |
| Ocelot (not used when `UseGateway` is false) | http://localhost:5282 |

## Production URLs (Azure App Service)

Same hosts as `appsettings.Production.json` / UI `wwwroot/appsettings.Production.json`. Stamps are part of the hostname; do not use `https://pk-ins-bff.azurewebsites.net` without the stamp.

| App | Production URL |
|---|---|
| UI (open this) | https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net/login |
| BFF (UI calls only this) | https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net |
| Claims | https://pk-ins-claims-gth7cvg6etb8hqa0.southindia-01.azurewebsites.net |
| Policies | https://pk-ins-policies-cqexgrdtgwbgc9cr.southindia-01.azurewebsites.net |
| Billing | https://pk-ins-billing-g5ecgqcmhmfaf7dr.southindia-01.azurewebsites.net |
| SignalR hub | https://pk-ins-bff-c7d5a5fzfsh7gzch.southindia-01.azurewebsites.net/hubs/notifications |

BFF CORS allows any `https://pk-ins-ui*.azurewebsites.net` origin. Ocelot is **not** deployed (`UseGateway: false`).
