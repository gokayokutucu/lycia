using System;
using Lycia.Messaging; // For EventBase

namespace Sample.OrderService.API.Dtos.IncomingPayment
{
    /// <summary>
    /// Represents the PaymentFailedEvent as consumed by OrderService.
    /// Structure should match the event published by PaymentService.
    /// </summary>
    public class PaymentFailedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }
    }
}
