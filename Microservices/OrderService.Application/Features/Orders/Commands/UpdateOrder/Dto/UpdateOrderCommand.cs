using MediatR;
using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record UpdateOrderCommand : IRequest<UpdateOrderCommandResult>
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public DateTime OrderDate { get; init; }
    public IEnumerable<OrderItem> OrderItems { get; init; }
    public decimal TotalAmount { get; init; }
    public static UpdateOrderCommand Create(Order orderEntity) 
        => new UpdateOrderCommand
        {
            OrderId = orderEntity.OrderId,
            CustomerId = orderEntity.CustomerId,
            OrderDate = orderEntity.OrderDate,
            OrderItems = orderEntity.OrderItems,
            TotalAmount = orderEntity.TotalAmount,
        };
}
