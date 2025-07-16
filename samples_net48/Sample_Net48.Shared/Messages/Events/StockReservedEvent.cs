using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Dtos;
using System;
using System.Collections.Generic;


namespace Sample_Net48.Shared.Messages.Events
{
    public sealed class StockReservedEvent : EventBase
    {
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public static StockReservedEvent Create(Guid orderId, Guid customerId, List<OrderItemDto> items)
            => new StockReservedEvent()
            {
                OrderId = orderId,
                CustomerId = customerId,
                Items = items
            };
    }
}
