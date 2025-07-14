using Lycia.Messaging;
using System;

namespace Sample_Net48.Shared.Messages.Events
{
    public class OrderDeliveredEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public DateTime DeliveredAt { get; set; } = DateTime.UtcNow;
    }
}