# Notification UI, inbox flags, and related changes

**Project:** `E:\pvkumar_git\insuranceplateform`  
**Date:** 28 Oct 2026

This note describes the work added after SignalR was already delivering live events. It covers the professional chrome (menu, header, shutter), SQL inbox flags, REST for read/delete, and extra publish events.

**Negotiate, `ws://...?id=`, users vs roles, who starts the hub:**  
`Mdfiles\SignalR.Negotiate.Uid.And.Groups.md`

---

## What you should see in the app

| Area | Behaviour |
| --- | --- |
| Left menu | Grouped: Overview / Work / Alerts. Brand mark. Sign out removed from the sidebar. |
| Top right | User chip (name). Click → **⏻ Sign out**. |
| Top right | Bell icon. Badge = unread count. Click → **shutter panel from the right**. |
| Shutter | Unread only, `OccurredUtc` descending. Preview text. **Read** / **Delete**. Link to the full page. |
| `/notifications` | Cards from SQL (not only in-memory hub list). Unread cards highlighted. Message truncated to **120** characters until **Read**. **⋯** menu: Read, Delete. |
| Delete | Soft delete. Row stays in SQL. UI never shows it again. |

---

## SQL (`Sql\004_NotificationFlags.sql`)

Run on `(localdb)\MSSQLLocalDB` / `DemoDataBase` (already applied on this machine):

```sql
ALTER TABLE dbo.InsuranceNotification ADD
    IsRead     BIT NOT NULL DEFAULT (0),
    IsDeleted  BIT NOT NULL DEFAULT (0),
    Active     BIT NOT NULL DEFAULT (1),
    ReadUtc    DATETIME2 NULL,
    DeletedUtc DATETIME2 NULL;
```

| Column | Meaning |
| --- | --- |
| `IsRead` | `0` unread, `1` after Read. `ReadUtc` set at that moment. |
| `IsDeleted` / `Active` | Delete sets `IsDeleted = 1`, `Active = 0`, `DeletedUtc = SYSUTCDATETIME()`. Queries use `Active = 1 AND IsDeleted = 0`. |

New inserts always start as unread and active (`IsRead = 0`, `IsDeleted = 0`, `Active = 1`).

---

## BFF APIs (cookie, not Ocelot)

`Authentication.Bff.Api\Controllers\NotificationsController.cs`  
Route prefix: `auth/notifications` (same cookie as login).

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/auth/notifications` | Active rows, newest first |
| GET | `/auth/notifications/unread` | `IsRead = 0` only |
| POST | `/auth/notifications/{id}/read` | Set `IsRead`, `ReadUtc` |
| POST | `/auth/notifications/{id}/delete` | Soft delete (`Active = 0`) |

Store methods in `Insurance.Helpers\SqlAdapterNotificationStore.cs`: `Insert`, `Recent(take, unreadOnly)`, `MarkRead`, `SoftDelete`. Insert still uses `ExecuteScalar` (not `SqlDataAdapter.Fill` on INSERT).

WASM client: `Insurance.UI.BlazorWasm\Services\NotificationApi.cs`. Registered in `Program.cs`.

DTO extras in `Insurance.Contracts\Notifications\InsuranceNotificationDto.cs`: `IsRead`, `IsDeleted`, `Active`, `ReadUtc`, `DeletedUtc`. Preview length constant: `NotificationEvents.PreviewLength = 120`.

---

## SignalR: more actions publish a message

`BffProxyController` still publishes **only after HTTP 2xx**. GETs are ignored.

| Browser / API action | EventType | Title (typical) |
| --- | --- | --- |
| POST `/api/claims` | `ClaimCreated` | Claim added |
| POST `/api/claims/{id}/approve` | `ClaimUpdated` | Claim approved |
| DELETE `/api/claims/{id}` | `ClaimDeleted` | Claim deleted |
| POST `/api/claimants` | `ClaimantCreated` | Claimant added |
| POST `/api/policies` | `PolicyCreated` | Policy added |

Message text prefers names/numbers from the JSON body (`claimNumber`, `claimantName`, `policyNumber`, `holderName`, `fullName`) when present.

Target is still `TargetRole = "Admin"` (same as before). Hub path is unchanged: `http://localhost:5281/hubs/notifications`.

---

## UI files

| File | Change |
| --- | --- |
| `Layout\NavMenu.razor` (+ `.css`) | Professional grouped nav, no logout in the menu |
| `Layout\MainLayout.razor` (+ `.css`) | Header bell + user menu; right shutter; starts the hub once; loads unread from SQL |
| `Pages\Notifications.razor` (+ `.css`) | Inbox cards, ⋯ Read/Delete, truncation, reload from SQL on hub events |
| `Layout\MainLayout.razor.css` | Darker sidebar, shutter animation (`translateX`) |

Logout lives only on the **name → power button** control.

---

## Visual Studio launch (from the Application Control / SelfHost issues)

These are separate from the inbox UI but were added so F5 does not start six debugged web hosts or the unsigned `.exe`:

| File | Purpose |
| --- | --- |
| `Directory.Build.props` | `UseAppHost=false` on BFF, Claims, Policies, Billing, Ocelot → VS/`dotnet` host the **DLL** |
| `InsurancePlatform.slnLaunch.user` | Profile **All services (debug UI)**: APIs start without debugger, UI starts with debugger. Also **BFF only** / **UI only**. |

Do not use the old profile that **Start**ed all six projects with the debugger (`SelfHostWebServer` duplicate key).

If Windows Smart App Control blocks `*.exe`, F5 of the apphost still fails; `dotnet …dll` or `UseAppHost=false` is the workaround.

---

## How to verify

1. Restart **BFF** and **Blazor UI** (and gateway + Claims/Policies if those are not already up).
2. Open `http://localhost:5280/login`, sign in as Praveen.
3. Confirm left menu grouping and **no** Sign out in the sidebar.
4. Click the name → ⏻ Sign out appears (do not sign out yet).
5. Add a claimant, add a claim, add a policy, approve a claim.
6. Bell badge should increase; shutter lists unread, newest first.
7. On `/notifications`, ⋯ → **Read** expands the full message; **Delete** hides the row. Reload the page: deleted rows stay gone; read rows stay marked read.
8. SQL check:

```sql
SELECT NotificationId, EventType, Title, Message, IsRead, IsDeleted, Active, ReadUtc, DeletedUtc, OccurredUtc
FROM dbo.InsuranceNotification
ORDER BY NotificationId DESC;
```

---

## Login 401 crash (what changed and how it protects)

On `/login` the console showed:

- `GET /auth/me` → 401
- `GET /auth/notifications/unread` → 401 then **unhandled exception**
- `GET /hubs/notifications` → 401 (`RequireAuthorization`)

`auth/me` 401 is expected with no cookie. The crash was **not** login itself. `MainLayout` always called `Hub.StartAsync()` and `GetUnreadAsync()`. `GetFromJsonAsync` throws on 401, so the yellow bar appeared on the login screen. That happens if the router still uses `MainLayout` for `[Authorize]` pages while showing `RedirectToLogin`.

### Code changes

| File | Change |
| --- | --- |
| `Layout\MainLayout.razor` | Start hub + unread **only after** `IsAuthenticated` |
| `Services\NotificationApi.cs` | 401/403 → return `[]`, do not throw |
| `Services\NotificationHubClient.cs` | Catch failed `StartAsync` (no cookie) |
| `App.razor` | Anonymous `NotAuthorized` uses `EmptyLayout`, so the shell (bell/hub) is not mounted |

### How it protects

1. **Anonymous user** — no hub, no unread call. Login can render. `auth/me` 401 is swallowed by `ApiAuthenticationStateProvider` (already returned anonymous).
2. **Failed HTTP** — inbox APIs never throw into the renderer; the UI shows an empty bell instead of dying.
3. **Failed hub negotiate** — connection is skipped until a later `StartAsync` after login (MainLayout runs again on `/` when authenticated).
4. **Cookie still required on the BFF** — `[Authorize]` on the hub and `auth/notifications` is unchanged. The UI just does not call them until a session exists. This is not a security bypass.

After sign-in, MainLayout sees an authenticated user, opens the hub with the cookie, and loads unread.

---

## What did not change

- Hub still on the BFF (`/hubs/notifications`), cookie auth, JWT not in the browser.
- Notifications.Api (generic later) was not implemented.
- Per-user read state was **not** added: `IsRead` is a single flag on the row (fine for this Admin demo).

---

## Full SignalR concept (point-wise) — what we discussed

Your picture is **almost** right, with two corrections:

- The user belongs to a **role in SQL**. On connect, **that user’s socket is added into the role group**. The group is a **set of connection ids**, not “one socket named Admin”.
- SignalR does **not** authenticate with the `id=` uid. It authenticates with the **`AppSession` cookie**. The uid only ties the WebSocket to the negotiate session.

### 1. Login (identity exists before any socket)

1. UI `POST /auth/login` → BFF checks `dbo.Users`.
2. BFF loads roles from `dbo.UserRoles` + `dbo.Roles` (e.g. Praveen → `Admin`).
3. BFF stores session in `dbo.Sessions` and sets HttpOnly cookie `AppSession`.
4. JWT stays **behind** the BFF (Ocelot). The browser never uses JWT for the hub.

### 2. Who opens the socket (not the notification page, not the BFF initiating)

1. After login, **every** signed-in page uses `MainLayout`.
2. `MainLayout` calls `NotificationHubClient.StartAsync()` so the **bell** works on Dashboard / Claims / Add claimant. You do **not** need `/notifications`.
3. The **browser** sends negotiate. The **BFF does not call itself**. The BFF only **hosts** `/hubs/notifications` and **answers**.
4. Login page must **not** start the hub (no cookie → 401 crash). That is already guarded.

### 3. How the request is generated (DevTools)

1. SignalR client inside WASM (not your Hub C# class) runs `StartAsync`.
2. **Step A — HTTP:** `POST http://localhost:5281/hubs/notifications/negotiate?negotiateVersion=1`  
   Cookie `AppSession` is sent (`credentials: include`).
3. **BFF reply JSON:** `connectionId`, `connectionToken`, transports (WebSockets).
4. **Step B — WebSocket:** `ws://localhost:5281/hubs/notifications?id=<connectionToken>`  
   Status `101 Switching Protocols`. Cookie is sent again on the upgrade.
5. `id=` is **`connectionToken`**. It is **not** `UserId`, **not** `Praveen`, **not** `Admin`. Reload → **new** `id`.
6. To see it: Network → **WS**, Preserve log, **reload while logged in**. Fetch/XHR will not show `ws://`.

### 4. What the uid is for (and what it is not)

1. Negotiate creates an in-memory hub connection on the BFF.
2. `?id=TOKEN` tells the server: **this socket belongs to that negotiate session**.
3. Without `id`, the server cannot attach the WebSocket to the right connection.
4. **Authentication is the cookie**, then `SessionCookieAuthHandler`. The uid is **not** a login token.

### 5. How SignalR authenticates (cookie → claims → UserIdentifier)

1. Hub is `MapHub(...).RequireAuthorization()`. No cookie → 401, no groups.
2. `SessionCookieAuthHandler` reads `AppSession`, loads the session user.
3. Claims:
   - `NameIdentifier` = **user name** (`Praveen`) → SignalR `Context.UserIdentifier` / `Clients.User("Praveen")`
   - `ClaimTypes.Role` = each SQL role (`Admin`)
   - `permission` = SQL permissions
4. Numeric `UserId` is **not** used as the SignalR user key.

### 6. How groups are created (one socket joins one or more groups)

On `InsuranceClaimHub.OnConnectedAsync`:

1. Abort if no `UserIdentifier`.
2. `Groups.AddToGroupAsync(connectionId, "user:Praveen")` — private inbox for that login.
3. For **each** role claim: `Groups.AddToGroupAsync(connectionId, "role:Admin")`.
4. Optional later: client `InvokeAsync("SubscribeToClaim", id)` → `claim:{id}` after `Claims.View` check.

The **server** adds groups. The UI cannot say “put me in Admin”.

### 7. Many users / many tabs

| Client | New negotiate `id`? | Groups joined |
| --- | --- | --- |
| Praveen tab 1 | Yes | `user:Praveen`, `role:Admin` |
| Praveen tab 2 | Yes, **different** `id` | same groups, second socket |
| Adjuster | Yes | `user:Adjuster`, `role:ClaimsAdjuster` |

Every logged-in user (every tab) sends negotiate and gets a **new** uid. A role group holds **all connection ids** currently in that role, not one shared socket.

### 8. Role-wise message handling (publish)

1. Business work is still **REST** (approve, add claim, add claimant, add policy).
2. After HTTP **2xx**, `BffProxyController` builds `InsuranceNotificationDto` (GET is ignored).
3. First-pass audience: `dto.TargetRole = "Admin"` (server-side rule, not chosen by the browser).
4. `NotificationPublisher`:
   - insert SQL history
   - if `TargetUserName` → `Clients.User(name)` → every socket of that login
   - if `TargetRole` → `Clients.Group("role:" + role)` → every socket in that role group
   - optional `claim:{id}`
5. Publisher does **not** send “to uid `kSyLebw4c-...`”. SignalR **fans out** to every connection currently in the user/group.
6. Only those sockets receive `ReceiveNotification`. Adjuster in `role:ClaimsAdjuster` does **not** get `role:Admin` live pushes.

### 9. UI receive

1. `HubConnection.On("ReceiveNotification")` in WASM.
2. Bell / shutter / page `StateHasChanged`.
3. Live path = groups. SQL inbox `GET /auth/notifications` is **not** role-filtered yet — another role can still **see the table** if they open the page. Live toast follows groups; backlog is a separate filter to add.

### 10. One diagram

```text
User in SQL  UserRoles  (Praveen → Admin)
        │
        ▼
Login → AppSession cookie
        │
        ▼
MainLayout (any signed-in page)
        │  browser, not BFF-initiated
        ▼
POST /hubs/notifications/negotiate   + cookie
        │  returns connectionToken
        ▼
ws://...?id=connectionToken          + cookie
        │  uid = this socket only
        ▼
SessionCookieAuthHandler (cookie = who you are)
        │
        ▼
OnConnectedAsync
   this connection id  →  group user:Praveen
   this connection id  →  group role:Admin
        │
        ▼
REST 200 (e.g. add claimant)
        │
        ▼
Publish TargetRole=Admin
   Clients.Group("role:Admin")
        │
        ▼
All sockets currently in role:Admin  (Praveen tab1 id, tab2 id)
   not Adjuster’s socket
        │
        ▼
ReceiveNotification in those browsers
```

### 11. Short answers to the questions we discussed

| Question | Answer |
| --- | --- |
| Why negotiate without opening Notifications? | `MainLayout` starts the hub for the bell. |
| Does BFF call negotiate? | No. Browser POST; BFF responds. |
| What is `id=`? | Per-socket `connectionToken` from negotiate. |
| Does uid authenticate? | No. Cookie does. Uid maps socket → connection. |
| New uid every user/tab? | Yes. |
| Role-wise group, message to relevant user? | User’s **socket is joined to** `role:{RoleName}`. Publish to that **group** reaches all connected members of the role. |
| SQL inbox same as live? | Not yet. Live is grouped; GET list is all active rows. |

### 12. Later: Azure SignalR

See **`Mdfiles\Azure.SignalR.Migration.md`**. Concept of `role:Admin` fan-out stays; sockets move to Azure; cookie is for negotiate/token, not for `wss://*.service.signalr.net`.

Order: Azure resource → `AddAzureSignalR` on BFF → `GET /auth/signalr-token` + WASM `AccessTokenProvider` → one BFF, then two BFFs.
