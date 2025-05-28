using System;
using Lycia.Messaging; // For EventBase

namespace Sample.PaymentService.API.Dtos.IncomingShipment
{
    /// <summary>
    /// Represents the ShipmentFailedEvent as consumed by PaymentService.
    /// Structure should match the event published by DeliveryService.
    /// </summary>
    public class ShipmentFailedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }
    }
}
