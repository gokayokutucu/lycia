using System;
using MediatR;

namespace OrderService.Domain.Events
{
    public class OrderCreatedDomainEvent : INotification
    {
        public Guid OrderId { get; }
        public Guid UserId { get; }
        public DateTime CreatedAt { get; }

        public OrderCreatedDomainEvent(Guid orderId, Guid userId, DateTime createdAt)
        {
            OrderId = orderId;
            UserId = userId;
            CreatedAt = createdAt;
        }
    }
}
