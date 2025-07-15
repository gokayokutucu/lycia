using Lycia.Messaging;
using System;

namespace Sample_Net481.Shared.Messages.Events
{
    public class OrderShippedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public DateTime ShippedAt { get; set; } = DateTime.UtcNow;
        public Guid ShipmentTrackId { get; set; }
    }
}