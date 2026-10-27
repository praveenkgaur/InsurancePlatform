using Insurance.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Authentication.Bff.Api.Hubs;

// Connection is rejected if cookie session is missing (see MapHub RequireAuthorization).
[Authorize]
public sealed class InsuranceClaimHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userName = Context.UserIdentifier; // NameIdentifier from handler
        if (string.IsNullOrWhiteSpace(userName))
        {
            Context.Abort();
            return;
        }

        // Private inbox: only this login's browsers.
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userName));

        // Role inbox: every connected Admin, every connected Adjuster, etc.
        // Server reads roles from the cookie session — client cannot fake them.
        foreach (var role in Context.User!.FindAll(ClaimTypes.Role).Select(c => c.Value))
            await Groups.AddToGroupAsync(Context.ConnectionId, RoleGroup(role));

        await base.OnConnectedAsync();
    }

    // Client may ASK to watch one claim. Server still checks permission.
    public async Task SubscribeToClaim(string claimId)
    {
        if (string.IsNullOrWhiteSpace(claimId))
            throw new HubException("Claim id is required.");

        if (Context.User?.HasClaim("permission", PermissionKeys.ClaimsView) != true)
            throw new HubException("You cannot subscribe to this claim.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ClaimGroup(claimId));
    }

    public Task UnsubscribeFromClaim(string claimId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ClaimGroup(claimId));

    public static string UserGroup(string userName) => "user:" + userName;
    public static string RoleGroup(string role) => "role:" + role;
    public static string ClaimGroup(string claimId) => "claim:" + claimId;
}