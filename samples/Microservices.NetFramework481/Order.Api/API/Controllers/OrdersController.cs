using MediatR;
using Sample.Order.NetFramework481.Application.Orders.UseCases.Commands.Create;
using Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;
using Sample.Order.NetFramework481.API.Filters;
using Shared.Contracts.Dtos;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace Sample.Order.NetFramework481.API.Controllers;

/// <summary>
/// Controller for order management operations.
/// </summary>
[RoutePrefix("api/orders")]
[GatewayOnlyFilter]
public sealed class OrdersController(IMediator mediator) : ApiController
{
    /// <summary>
    /// Creates a new order.
    /// </summary>
    [HttpPost]
    [Route("{customerId}")]
    public async Task<IHttpActionResult> CreateOrder(Guid customerId, Guid addressId, Guid cardId, [FromBody] List<OrderItemDto> command, CancellationToken cancellationToken)
    {
        await mediator.Send(CreateOrderCommand.Create(customerId, addressId, cardId, command), cancellationToken);
        return Ok(new { Message = "Order creation initiated successfully" });
    }

    /// <summary>
    /// Gets a specific order by ID.
    /// </summary>
    [HttpGet]
    [Route("{id}", Name = "GetOrder")]
    public async Task<IHttpActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(GetOrderQuery.Create(id), cancellationToken);
        return Ok(result);
    }
}
