using Authentication.Bff.Api.Hubs;
using Insurance.Contracts.Notifications;
using Insurance.Helpers;
using Microsoft.AspNetCore.SignalR;

namespace Authentication.Bff.Api.Services;

public sealed class NotificationPublisher
{
    private readonly IHubContext<InsuranceClaimHub> _hub;
    private readonly SqlAdapterNotificationStore _store;
    private readonly IConfiguration _config;

    public NotificationPublisher(
        IHubContext<InsuranceClaimHub> hub,
        SqlAdapterNotificationStore store,
        IConfiguration config)
    {
        _hub = hub;
        _store = store;
        _config = config;
    }

    public async Task PublishAsync(InsuranceNotificationDto dto, CancellationToken ct = default)
    {
        if (!_config.GetValue("SignalR:Enabled", true))
            return;

        dto.OccurredUtc = DateTime.UtcNow;
        if (_config.GetValue("SignalR:PersistToSql", true))
            _store.Insert(dto); // history for refresh; SignalR is not a durable queue

        var method = _config["SignalR:ClientMethodName"] ?? NotificationEvents.ClientMethod;

        // One named user (Praveen). Other logins do not get this.
        if (!string.IsNullOrWhiteSpace(dto.TargetUserName))
            await _hub.Clients.User(dto.TargetUserName).SendAsync(method, dto, ct);

        // All currently connected users with that role.
        if (!string.IsNullOrWhiteSpace(dto.TargetRole))
            await _hub.Clients.Group(InsuranceClaimHub.RoleGroup(dto.TargetRole)).SendAsync(method, dto, ct);

        // Optional: everyone watching this claim id (after SubscribeToClaim).
        if (dto.EntityName == "InsuranceClaim" && !string.IsNullOrWhiteSpace(dto.EntityId))
            await _hub.Clients.Group(InsuranceClaimHub.ClaimGroup(dto.EntityId)).SendAsync(method, dto, ct);
    }
}