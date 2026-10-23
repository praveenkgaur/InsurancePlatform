# Insurance Platform (BFF cookie + Ocelot)

How the UI, APIs, cookies, JWT, and HttpClientFactory run is in [Mdfiles/InsurancePlatform.HowItWorks.md](Mdfiles/InsurancePlatform.HowItWorks.md).

Blazor WebAssembly talks only to `Authentication.Bff.Api`. Login sets an HttpOnly cookie (`AppSession` on HTTP, `__Host-AppSession` on HTTPS). The BFF stores the JWT and sends `Authorization: Bearer` to Ocelot. Claims, Policies, and Billing APIs validate the JWT and permission policies.

## Demo users

- `Praveen` / `Password@123` — Admin (view, approve, delete)
- `Adjuster` / `Password@123` — ClaimsAdjuster (view and approve only)

## Run order

SQL scripts are already applied to LocalDB `DemoDataBase`.

From five terminals under `D:\SampleProjects\GenAiUIApi\BffProjectWithCookie`:

```
dotnet run --project Billing.Api
dotnet run --project Policies.Api
dotnet run --project Claims.Api
dotnet run --project Ocelot.Gateway
dotnet run --project Authentication.Bff.Api
dotnet run --project Insurance.UI.BlazorWasm
```

Open http://localhost:5280/login

The UI calls only the BFF at http://localhost:5281. The BFF attaches the JWT and calls Ocelot at http://localhost:5282.
