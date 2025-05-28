using System;
using Lycia.Messaging; // For EventBase

namespace Sample.PaymentService.API.Events
{
    public class PaymentFailedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }

        public PaymentFailedEvent(Guid sagaId, Guid orderId, string reason)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            Reason = reason;
        }
    }
}
