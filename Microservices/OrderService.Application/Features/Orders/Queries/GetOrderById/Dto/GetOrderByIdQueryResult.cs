using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Queries;

public sealed record GetOrderByIdQueryResult()
{
    public Order Order { get; init; }
    public static GetOrderByIdQueryResult Create(Order orderEntity) 
        => new GetOrderByIdQueryResult
        {
            Order = orderEntity
        };
}
