using System;
using Lycia.Messaging; // For EventBase

namespace Sample.OrderService.API.Events
{
    /// <summary>
    /// Event published when an order has been successfully cancelled.
    /// </summary>
    public class OrderCancelledEvent : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string ReasonForCancellation { get; set; }

        public OrderCancelledEvent(Guid sagaId, Guid orderId, string reasonForCancellation)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            ReasonForCancellation = reasonForCancellation;
        }
    }
}
