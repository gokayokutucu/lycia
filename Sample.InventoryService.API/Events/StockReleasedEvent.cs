using System;
using System.Collections.Generic;
using Lycia.Messaging; // For EventBase
using Sample.InventoryService.API.Dtos; // For ReleasedItemDto

namespace Sample.InventoryService.API.Events
{
    /// <summary>
    /// Event published when previously reserved stock has been released.
    /// </summary>
    public class StockReleasedEvent : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public List<ReleasedItemDto> ItemsReleased { get; set; }

        public StockReleasedEvent(Guid sagaId, Guid orderId, List<ReleasedItemDto> itemsReleased)
        {
            this.SagaId = sagaId;
            OrderId = orderId;
            ItemsReleased = itemsReleased ?? new List<ReleasedItemDto>();
        }
    }
}
