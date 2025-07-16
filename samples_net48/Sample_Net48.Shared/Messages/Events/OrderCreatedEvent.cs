using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Dtos;
using System;
using System.Collections.Generic;

namespace Sample_Net48.Shared.Messages.Events
{
    public sealed class OrderCreatedEvent : EventBase
    {
        public Guid CustomerId { get; private set; }
        public Guid OrderId { get; private set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public decimal OrderTotal { get; private set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();

        public static OrderCreatedEvent Create(Guid orderId, Guid customerId, string shippingAddress, decimal orderTotal, List<OrderItemDto> items)
            => new OrderCreatedEvent
            {
                OrderId = orderId,
                CustomerId = customerId,
                ShippingAddress = shippingAddress,
                OrderTotal = orderTotal,
                Items = items
            };
    }
}
