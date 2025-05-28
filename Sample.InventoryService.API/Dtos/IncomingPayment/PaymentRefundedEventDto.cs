using System;
using Lycia.Messaging; // For EventBase

namespace Sample.InventoryService.API.Dtos.IncomingPayment
{
    /// <summary>
    /// Represents the PaymentRefundedEvent as consumed by InventoryService.
    /// Structure should match the event published by PaymentService.
    /// </summary>
    public class PaymentRefundedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string RefundTransactionId { get; set; }
    }
}
