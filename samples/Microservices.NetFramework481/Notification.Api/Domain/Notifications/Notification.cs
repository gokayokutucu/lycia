using Sample.Notification.NetFramework481.Domain.Common;
using System;

namespace Sample.Notification.NetFramework481.Domain.Notifications;

public sealed class Notification : Entity
{
    public string Recipient { get; set; }
    public NotificationType Type { get; set; }
    public string Subject { get; set; }
    public string Message { get; set; }
    public NotificationStatus Status { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string RelatedEntityType { get; set; }
    public DateTime? SentAt { get; set; }
}
