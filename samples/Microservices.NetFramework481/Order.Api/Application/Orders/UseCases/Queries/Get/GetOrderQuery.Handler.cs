using System;
using System.Threading;
using System.Threading.Tasks;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;
using Sample.Order.NetFramework481.Application.Interfaces;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;

/// <summary>
/// Handler for GetOrderQuery.
/// </summary>
public sealed class GetOrderQueryHandler(IOrderRepository orderRepository, ILogger<GetOrderQueryHandler> logger) : IRequestHandler<GetOrderQuery, GetOrderQueryResult?>
{
    /// <summary>
    /// Handles the query.
    /// </summary>
    public async Task<GetOrderQueryResult?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving order: {OrderId}", request.OrderId);

        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
        {
            logger.LogWarning("Order not found: {OrderId}", request.OrderId);
            return null;
        }

        return order.Adapt<GetOrderQueryResult>();
    }
}
