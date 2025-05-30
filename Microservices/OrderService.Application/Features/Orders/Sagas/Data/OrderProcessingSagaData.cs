using System;
using System.Collections.Generic;
using Lycia.Saga; // For SagaData
using OrderService.Application.Features.Orders.Sagas.Commands; // For OrderItemSagaDto

namespace OrderService.Application.Features.Orders.Sagas.Data
{
    public class OrderProcessingSagaData : SagaData
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public string OrderStatus { get; set; } // e.g., "ProcessingStarted", "PaymentConfirmed", "InventoryUpdated"
        public decimal TotalPrice { get; set; }
        public List<OrderItemSagaDto> Items { get; set; } = new List<OrderItemSagaDto>();

        // Default constructor
        public OrderProcessingSagaData() { }

        // Constructor for initialization if needed (SagaData is often initialized by handlers)
        public OrderProcessingSagaData(Guid sagaId, Guid orderId, Guid userId, decimal totalPrice, List<OrderItemSagaDto> items)
        {
            Id = sagaId; // TODO: Gop - SagaData.Id is the SagaInstanceId
            OrderId = orderId;
            UserId = userId;
            TotalPrice = totalPrice;
            Items = items ?? new List<OrderItemSagaDto>();
            OrderStatus = "SagaInitialized"; // Initial status
        }
    }
}
