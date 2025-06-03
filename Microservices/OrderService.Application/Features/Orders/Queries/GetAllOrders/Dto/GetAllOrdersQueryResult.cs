using OrderService.Domain.Entities;

namespace OrderService.Application.Features.Orders.Queries;

public sealed record GetAllOrdersQueryResult()
{
    public IEnumerable<Order> Orders { get; init; }
    public static GetAllOrdersQueryResult Create(IEnumerable<Order> orderEntities) 
        => new GetAllOrdersQueryResult
        {
            Orders = orderEntities
        };
}