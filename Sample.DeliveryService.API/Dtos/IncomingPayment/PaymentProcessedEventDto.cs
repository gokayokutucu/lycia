using System;
using Lycia.Messaging; // For EventBase

namespace Sample.DeliveryService.API.Dtos.IncomingPayment
{
    /// <summary>
    /// Represents the PaymentProcessedEvent as consumed by DeliveryService.
    /// Structure should match the event published by PaymentService.
    /// </summary>
    public class PaymentProcessedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string PaymentConfirmationId { get; set; }
    }
}
