using System;
using System.Collections.Generic;
using OrderService.Application.Features.Orders.Sagas.Commands; // For OrderItemSagaDto

namespace OrderService.Application.Features.Orders.Sagas.Events
{
    // This event is intended for publishing to an external message broker (e.g., RabbitMQ)
    // It's a plain C# object (POCO) that will be serialized (e.g., to JSON).
    public class OrderCreationConfirmedEvent
    {
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public List<OrderItemSagaDto> Items { get; set; } = new List<OrderItemSagaDto>();
        public DateTime ConfirmedAt { get; set; }

        // Parameterless constructor for deserialization
        public OrderCreationConfirmedEvent() {}

        public OrderCreationConfirmedEvent(Guid orderId, Guid userId, decimal totalPrice, List<OrderItemSagaDto> items, DateTime confirmedAt)
        {
            OrderId = orderId;
            UserId = userId;
            TotalPrice = totalPrice;
            Items = items ?? new List<OrderItemSagaDto>();
            ConfirmedAt = confirmedAt;
        }
    }
}
