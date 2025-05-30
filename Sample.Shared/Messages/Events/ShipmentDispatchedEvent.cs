using System;
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events
{
    public class ShipmentDispatchedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string TrackingNumber { get; set; }
        public DateTime DispatchDate { get; set; }
    }
}
