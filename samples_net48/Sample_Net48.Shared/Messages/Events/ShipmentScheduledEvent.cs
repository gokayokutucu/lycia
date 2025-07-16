using Lycia.Messaging;
using Sample_Net48.Shared.Messages.Dtos;
using System;
using System.Collections.Generic;


namespace Sample_Net48.Shared.Messages.Events
{
    public sealed class ShipmentScheduledEvent : EventBase
    {
        public Guid OrderId { get; private set; }
        public Guid CustomerId { get; private set; }
        public string Destination { get; private set; } = string.Empty;
        public List<OrderItemDto> Items { get; private set; } = new List<OrderItemDto>();

        public static ShipmentScheduledEvent Create(Guid orderId, Guid customerId, string destination, List<OrderItemDto> items)
            => new ShipmentScheduledEvent()
            {
                OrderId = orderId,
                CustomerId = customerId,
                Destination = destination,
                Items = items
            };
    }
}