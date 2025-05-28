using System;
using System.Collections.Generic;
using Lycia.Messaging; // For EventBase
using Sample.InventoryService.API.Dtos; // For ReservedItemDto

namespace Sample.InventoryService.API.Events
{
    public class StockReservedEvent : EventBase
    {
        public Guid OrderId { get; set; }
        public List<ReservedItemDto> ItemsReserved { get; set; }

        public StockReservedEvent(Guid sagaId, Guid orderId, List<ReservedItemDto> itemsReserved)
        {
            this.SagaId = sagaId; // Set inherited SagaId
            OrderId = orderId;
            ItemsReserved = itemsReserved ?? new List<ReservedItemDto>();
        }
    }
}
