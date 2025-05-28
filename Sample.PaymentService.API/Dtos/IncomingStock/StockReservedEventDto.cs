using System;
using System.Collections.Generic;
using Lycia.Messaging; // For EventBase

namespace Sample.PaymentService.API.Dtos.IncomingStock
{
    /// <summary>
    /// Represents the StockReservedEvent as consumed by PaymentService.
    /// Structure should match the event published by InventoryService.
    /// </summary>
    public class StockReservedEventDto : EventBase // Inherits SagaId, MessageId, Timestamp, ApplicationId
    {
        public Guid OrderId { get; set; }
        public List<ReservedItemDto> ItemsReserved { get; set; }

        public StockReservedEventDto()
        {
            ItemsReserved = new List<ReservedItemDto>();
        }
    }
}
