using System;
using System.Collections.Generic;
using Lycia.Messaging; // For EventBase

namespace Sample.OrderService.API.Dtos.IncomingInventory
{
    /// <summary>
    /// Represents the StockReleasedEvent as consumed by OrderService.
    /// Structure should match the event published by InventoryService.
    /// </summary>
    public class StockReleasedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public List<ReleasedItemDetailDto> ItemsReleased { get; set; } // Using the local DTO

        public StockReleasedEventDto()
        {
            ItemsReleased = new List<ReleasedItemDetailDto>();
        }
    }
}
