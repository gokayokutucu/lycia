using System;
using Lycia.Messaging; // For EventBase

namespace Sample.InventoryService.API.Events
{
    public class StockReservationFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }

        public StockReservationFailedEvent(Guid sagaId, Guid orderId, string reason)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            Reason = reason;
        }
    }
}
