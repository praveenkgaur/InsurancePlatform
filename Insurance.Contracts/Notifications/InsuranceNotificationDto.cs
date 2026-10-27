using System;
using System.Collections.Generic;
using System.Text;

namespace Insurance.Contracts.Notifications
{
    // Small payload only. Do not send the whole claim row or secrets.
    public sealed class InsuranceNotificationDto
    {
        public int NotificationId { get; set; }
        public string EventType { get; set; } = "";      // ClaimUpdated, ClaimDeleted, ManagerAlert
        public string EntityName { get; set; } = "";     // InsuranceClaim
        public string EntityId { get; set; } = "";       // claim id as string
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? TargetUserName { get; set; }
        public string? TargetRole { get; set; }
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    }
    public static class NotificationEvents
    {
        // MUST match Blazor: hub.On<...>(NotificationEvents.ClientMethod, ...)
        public const string ClientMethod = "ReceiveNotification";
    }

}
