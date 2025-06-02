using System;
using System.Collections.Generic;
using MediatR;
using OrderService.Domain.Events.Dtos; // For OrderItemDomainDto

namespace OrderService.Domain.Events
{
    public class OrderCreatedDomainEvent : INotification
    {
        public Guid OrderId { get; }
        public Guid UserId { get; }
        public decimal TotalPrice { get; }
        public List<OrderItemDomainDto> Items { get; }
        public DateTime CreatedAt { get; }

        public OrderCreatedDomainEvent(Guid orderId, Guid userId, decimal totalPrice, List<OrderItemDomainDto> items, DateTime createdAt)
        {
            OrderId = orderId;
            UserId = userId;
            TotalPrice = totalPrice;
            Items = items ?? new List<OrderItemDomainDto>();
            CreatedAt = createdAt;
        }
    }
}
