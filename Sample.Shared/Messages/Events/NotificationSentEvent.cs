using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class NotificationSentEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string NotificationType { get; set; } // e.g., "OrderConfirmation", "ShipmentUpdate"
    }
}
