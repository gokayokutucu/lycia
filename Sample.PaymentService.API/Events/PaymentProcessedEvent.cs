using System;
using Lycia.Messaging; // For EventBase

namespace Sample.PaymentService.API.Events
{
    public class PaymentProcessedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public string PaymentConfirmationId { get; set; }

        public PaymentProcessedEvent(Guid sagaId, Guid orderId, string paymentConfirmationId)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            PaymentConfirmationId = paymentConfirmationId;
        }
    }
}
