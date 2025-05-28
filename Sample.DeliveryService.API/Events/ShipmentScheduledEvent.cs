using System;
using Lycia.Messaging; // For EventBase

namespace Sample.DeliveryService.API.Events
{
    public class ShipmentScheduledEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string ShipmentTrackingId { get; set; }

        public ShipmentScheduledEvent(Guid sagaId, Guid orderId, string shipmentTrackingId)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            ShipmentTrackingId = shipmentTrackingId;
        }
    }
}
