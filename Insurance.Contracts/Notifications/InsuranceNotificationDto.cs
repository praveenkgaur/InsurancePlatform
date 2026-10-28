namespace Insurance.Contracts.Notifications;

public sealed class InsuranceNotificationDto
{
    public int NotificationId { get; set; }
    public string EventType { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? TargetUserName { get; set; }
    public string? TargetRole { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public bool IsDeleted { get; set; }
    public bool Active { get; set; } = true;
    public DateTime? ReadUtc { get; set; }
    public DateTime? DeletedUtc { get; set; }
}

public static class NotificationEvents
{
    public const string ClientMethod = "ReceiveNotification";
    public const int PreviewLength = 120;
}
