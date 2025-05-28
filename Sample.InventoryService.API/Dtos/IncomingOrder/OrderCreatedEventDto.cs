using System;
using Lycia.Messaging; // For EventBase

namespace Sample.InventoryService.API.Dtos.IncomingOrder
{
    /// <summary>
    /// Represents the OrderCreatedEvent as consumed by InventoryService.
    /// Structure should match the event published by OrderService.
    /// </summary>
    public class OrderCreatedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public OrderDetailsDto OrderDetails { get; set; }
    }
}
