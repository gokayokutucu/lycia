using MediatR;

namespace OrderService.Domain.Events;

public sealed record OrderCreatedDomainEvent : INotification
{
    public Guid OrderId { get; init; }
    public Guid UserId { get; init; }
    public DateTime CreatedAt { get; init; }

    public static OrderCreatedDomainEvent Create(Guid orderId, Guid userId, DateTime createdAt) 
        => new OrderCreatedDomainEvent
        {
            OrderId = orderId,
            UserId = userId,
            CreatedAt = createdAt
        };
}