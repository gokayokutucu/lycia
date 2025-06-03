using MediatR;
using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Commands;

public sealed record AddOrderCommand : IRequest<AddOrderCommandResult>
{
    public Guid CustomerId { get; init; }
    public DateTime OrderDate { get; init; }
    public IEnumerable<OrderItem> OrderItems { get; init; }
    public decimal TotalAmount { get; init; }
    public static AddOrderCommand Create(Order orderEntity) 
        => new AddOrderCommand
        {
            CustomerId = orderEntity.CustomerId,
            OrderDate = orderEntity.OrderDate,
            OrderItems = orderEntity.OrderItems,
            TotalAmount = orderEntity.TotalAmount,
        };
}