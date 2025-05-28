using System;
using Lycia.Messaging; // For EventBase

namespace Sample.DeliveryService.API.Events
{
    public class ShipmentFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }

        public ShipmentFailedEvent(Guid sagaId, Guid orderId, string reason)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            Reason = reason;
        }
    }
}
