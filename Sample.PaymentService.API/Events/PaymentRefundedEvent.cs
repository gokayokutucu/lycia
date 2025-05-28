using System;
using Lycia.Messaging; // For EventBase

namespace Sample.PaymentService.API.Events
{
    /// <summary>
    /// Event published when a payment has been successfully refunded.
    /// </summary>
    public class PaymentRefundedEvent : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string RefundTransactionId { get; set; }

        public PaymentRefundedEvent(Guid sagaId, Guid orderId, string refundTransactionId)
        {
            this.SagaId = sagaId;
            OrderId = orderId;
            RefundTransactionId = refundTransactionId;
        }
    }
}
