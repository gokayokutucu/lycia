using System;
using Lycia.Messaging; // For EventBase

namespace Sample.OrderService.API.Dtos.IncomingInventory
{
    /// <summary>
    /// Represents the StockReservationFailedEvent as consumed by OrderService.
    /// Structure should match the event published by InventoryService.
    /// </summary>
    public class StockReservationFailedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }
    }
}
