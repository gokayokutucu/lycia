using Sample.Notification.NetFramework481.Domain.Common;
using System;

namespace Sample.Notification.NetFramework481.Domain.Notifications;

public sealed class Notification : Entity
{
    public string Recipient { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
}
