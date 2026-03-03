using System;
using Lycia.Saga.Messaging;
using Shared.Contracts.Enums;

namespace Shared.Contracts.Events.Notification;

public sealed class CustomerNotifiedEvent : EventBase
{
    public CustomerNotifiedEvent()
    {
        
    }
    public Guid OrderId { get; set; }
    public string NotificationId { get; set; } = string.Empty;
    public NotificationType NotificationType { get; set; } = NotificationType.Email;
}
