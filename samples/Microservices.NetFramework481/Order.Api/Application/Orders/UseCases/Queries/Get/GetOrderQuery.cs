using System;
using MediatR;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;

/// <summary>
/// Query to retrieve a single order by ID.
/// </summary>
public sealed class GetOrderQuery : IRequest<GetOrderQueryResult?>
{
    /// <summary>
    /// Order ID to retrieve.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// Creates a new instance of GetOrderQuery.
    /// </summary>
    public static GetOrderQuery Create(Guid orderId) => new() { OrderId = orderId };
}
