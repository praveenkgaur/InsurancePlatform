# Azure SignalR — migrate later (API + UI code)

**Do not do this while one BFF instance on localhost is enough.**  
When you run **two or more** `Authentication.Bff.Api` processes, in-memory sockets on BFF1 cannot receive `SendAsync` from BFF2. Azure SignalR holds the sockets and fans out.

Hub **code** stays on the BFF. Azure SignalR is not a second Hub and is **not** Notifications.Api.

---

## Does the concept change?

| Concept now | After Azure SignalR |
| --- | --- |
| Browser starts negotiate | **Same** (`MainLayout` → `StartAsync`) |
| Groups `user:{name}`, `role:{role}` | **Same** (`OnConnectedAsync`) |
| Publish `Clients.Group("role:Admin")` | **Same** — all Admin sockets, even on another BFF |
| `?id=` connection token | **Same idea**, issued in the Azure negotiate path |
| One Admin group, all Admin tabs get the message | **Same** |
| SQL inbox / `IsRead` | **Unchanged** |
| Cookie on the **WebSocket host** | **Changes** — socket host is `*.service.signalr.net`, not `:5281` |
| Where the socket lives | **Changes** — Azure memory/network, not only one BFF process |

**What does not change:** REST still goes BFF → Ocelot → Claims/Policies. Publisher still runs in the BFF after 2xx. Clients still must not choose `TargetRole`.

**What does change:** after negotiate, the browser opens **`wss://<name>.service.signalr.net`**. The `AppSession` cookie is **not** sent there. Identity must ride in the **negotiate** (cookie to BFF) and/or a **short JWT** (`AccessTokenProvider`).

Use **Default** mode (hub stays in the BFF). Do **not** use Serverless unless you rewrite the Hub to Azure Functions.

---

## Order to implement

1. **Azure SignalR resource + connection string**  
   Portal → SignalR Service → Default mode → copy connection string → Key Vault / App Settings (not git). CORS on the resource: UI origin (`https://your-ui` and `http://localhost:5280` for dev). BFF outbound HTTPS to `*.service.signalr.net`.

2. **`AddAzureSignalR` on BFF**  
   Package + `Program.cs` + config. Keep `MapHub("/hubs/notifications")`. Empty connection string = local in-process SignalR (laptop).

3. **`GET /auth/signalr-token` + WASM `AccessTokenProvider`**  
   Cookie still used to **issue** the token. Token carries `NameIdentifier` + roles (minutes, not hours). HubConnection sends that token to Azure. Cookie handler stays for negotiate to the BFF.

4. **Prove one BFF**, then **scale to 2 BFF instances**  
   Two Admins (or two tabs) on different instances; approve a claim; **both** still get `ReceiveNotification`. If only one gets it, Azure is not wired (still in-process).

---

## 1) Resource + connection string

BFF `appsettings.json` (secret in User Secrets / Key Vault in real env):

```json
"Azure": {
  "SignalR": {
    "ConnectionString": ""
  }
},
"Jwt": {
  "Issuer": "InsurancePlatform",
  "Audience": "InsuranceApis",
  "SignalRAudience": "InsuranceSignalR",
  "SigningKey": "InsurancePlatform-dev-signing-key-32ch",
  "AccessTokenMinutes": 30,
  "SignalRTokenMinutes": 5
}
```

Empty `ConnectionString` → do not call `AddAzureSignalR` (current localhost behaviour).

---

## 2) API / BFF code

### Package

```xml
<PackageReference Include="Microsoft.Azure.SignalR" Version="1.32.0" />
```

(Use the current stable when you migrate.)

### `Authentication.Bff.Api\Program.cs`

```csharp
builder.Services.AddSignalR();
var azureSignalR = builder.Configuration["Azure:SignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalR))
    builder.Services.AddSignalR().AddAzureSignalR();

// existing cookie scheme + MapHub stay:
app.MapHub<InsuranceClaimHub>("/hubs/notifications").RequireAuthorization();
```

Prefer one `AddSignalR()` chain:

```csharp
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Azure:SignalR:ConnectionString"]))
    signalR.AddAzureSignalR();
```

`InsuranceClaimHub` — **no group logic change**.  
`NotificationPublisher` — **no SendAsync change**.

### JWT must include `NameIdentifier`

`Clients.User("Praveen")` and `Context.UserIdentifier` need `ClaimTypes.NameIdentifier`. Add it on the **SignalR** token (today’s access token for Ocelot uses `Sub` / `Name` — do not reuse that audience for Azure).

In `JwtTokenFactory` add a method (paste when migrating):

```csharp
public string CreateSignalRToken(string userName, IEnumerable<string> roles, IEnumerable<string> permissions)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, userName), // required for Clients.User / UserIdentifier
        new(ClaimTypes.Name, userName),
        new(JwtRegisteredClaimNames.Sub, userName)
    };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
    claims.AddRange(permissions.Select(p => new Claim("permission", p)));

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    var token = new JwtSecurityToken(
        _options.Issuer,
        _options.SignalRAudience, // "InsuranceSignalR" — not InsuranceApis
        claims,
        expires: DateTime.UtcNow.AddMinutes(_options.SignalRTokenMinutes),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

### `GET /auth/signalr-token` (cookie required)

New action on `AuthController` (or a small controller). Same cookie as login. **Do not** return the Ocelot JWT.

```csharp
[HttpGet("signalr-token")]
public async Task<ActionResult<object>> SignalRToken()
{
    if (!TryReadSession(out var sessionId, out var rawToken))
        return Unauthorized();
    var user = await _login.GetCurrentAsync(sessionId, rawToken);
    if (user is null)
        return Unauthorized();

    var jwt = _tokens.CreateSignalRToken(user.UserName, user.Roles, user.Permissions);
    return Ok(new { accessToken = jwt, expiresInSeconds = 300 });
}
```

Inject `JwtTokenFactory` / `LoginService` as you already do for login.

If you also validate JWT on the hub (optional extra), add a second scheme and:

```csharp
app.MapHub<InsuranceClaimHub>("/hubs/notifications")
    .RequireAuthorization(); // cookie on negotiate; Azure token on service connection
```

Default Azure SignalR (non-serverless): **negotiate hits the BFF with cookie**; the SDK then gives the client an **Azure service token**. `AccessTokenProvider` is still recommended so reconnects and WASM cross-origin stay explicit.

---

## 3) UI code (`NotificationHubClient`)

`wwwroot/appsettings.json` — Hub URL **stays the BFF** (not the Azure hostname):

```json
"SignalR": {
  "HubUrl": "http://localhost:5281/hubs/notifications",
  "ClientMethodName": "ReceiveNotification"
}
```

After migrate, HubUrl = `https://<bff-host>/hubs/notifications`. The client library follows Azure’s URL from negotiate.

Replace `StartAsync` WithUrl section:

```csharp
_hubConnection = new HubConnectionBuilder()
    .WithUrl(url, options =>
    {
        // Negotiate to BFF still needs the session cookie.
        options.HttpMessageHandlerFactory = inner => new CookieSendingHandler { InnerHandler = inner };
        // Azure WebSocket / reconnect: short JWT from BFF (cookie-protected).
        options.AccessTokenProvider = async () =>
        {
            var http = _http; // inject IHttpClientFactory "Bff" or reuse cookie HttpClient
            var token = await http.GetFromJsonAsync<SignalRTokenDto>("auth/signalr-token");
            return token?.AccessToken;
        };
    })
    .WithAutomaticReconnect()
    .Build();
```

```csharp
public sealed class SignalRTokenDto
{
    public string AccessToken { get; set; } = "";
    public int ExpiresInSeconds { get; set; }
}
```

Inject `HttpClient` (the existing BFF client with `CookieHandler`) into `NotificationHubClient` so `auth/signalr-token` sends `AppSession`.

Keep the try/catch around `StartAsync` for the login page.

**Do not** set `HubUrl` to `wss://xxx.service.signalr.net` in the UI.

---

## 4) Prove, then scale

| Check | Expected |
| --- | --- |
| Network | `POST {bff}/hubs/notifications/negotiate` then **WS to `*.service.signalr.net`** (not only `:5281`) |
| One BFF, two Admin tabs | Both get `ReceiveNotification` |
| **Two BFF** instances, load-balanced UI | Approve on any API path; **both** Admin tabs get the message |
| Adjuster tab | Still no live Admin-targeted event |
| Logout | Token endpoint 401; new negotiate fails |

If WS still goes only to `:5281`, `AddAzureSignalR` is not active (empty connection string).

---

## Files you will touch (checklist)

| Side | File | Change |
| --- | --- | --- |
| API | `Authentication.Bff.Api.csproj` | Package `Microsoft.Azure.SignalR` |
| API | `Program.cs` | `AddAzureSignalR` when connection string set |
| API | `appsettings` / Key Vault | `Azure:SignalR:ConnectionString` |
| API | `JwtTokenFactory` + `JwtOptions` | `CreateSignalRToken` + `NameIdentifier` + `SignalRAudience` |
| API | `AuthController` | `GET auth/signalr-token` |
| API | `InsuranceClaimHub` | **None** (groups unchanged) |
| API | `NotificationPublisher` | **None** |
| UI | `NotificationHubClient.cs` | `AccessTokenProvider` + existing cookie handler |
| UI | `wwwroot/appsettings.json` | HubUrl still **BFF** |
| Azure | SignalR resource CORS | UI origin |

---

## Concept in one picture (after migrate)

```text
Browser  MainLayout.StartAsync
   POST  BFF /hubs/notifications/negotiate     + AppSession cookie
   GET   BFF /auth/signalr-token               + cookie  → short JWT
BFF      Azure SDK returns service endpoint
Browser  wss://<name>.service.signalr.net      + Azure/JWT  (no cookie)
Azure    keeps sockets; invokes Hub on any BFF
BFF      OnConnectedAsync → role:Admin, user:Praveen
BFF      Clients.Group("role:Admin").SendAsync(...)
Azure    delivers to every Admin socket (BFF1 or BFF2)
```

Groups and Admin-fanout **concept stay**. Only **where the socket is stored** and **how the socket is authenticated after negotiate** change.
