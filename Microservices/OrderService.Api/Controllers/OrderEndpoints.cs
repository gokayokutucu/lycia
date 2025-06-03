using MediatR;
using OrderService.Application.Features.Orders.Commands;
using OrderService.Application.Features.Orders.Queries;
using OrderService.Domain.Entities;
namespace OrderService.Api.Controllers;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/shopping-cart").WithTags(nameof(Order));

        group.MapGet("/", async (IMediator mediator) =>
        {
            var query = GetAllOrdersQuery.Create();
            return await mediator.Send(query);
        })
        .WithName("GetAllOrders")
        .WithOpenApi();

        group.MapGet("/{id}", async (IMediator mediator, Guid id) =>
        {
            var query = GetOrderByIdQuery.Create(id);
            return await mediator.Send(query);
        })
        .WithName("GetOrderById")
        .WithOpenApi();

        group.MapPut("/{id}", async (IMediator mediator, Guid id, Order order) =>
        {
            var command = UpdateOrderCommand.Create(order with { OrderId = id });
            return await mediator.Send(command);
        })
        .WithName("UpdateOrder")
        .WithOpenApi();

        group.MapPost("/", async (IMediator mediator, Order order) =>
        {
            var command = AddOrderCommand.Create(order);
            return await mediator.Send(command);
        })
        .WithName("CreateOrder")
        .WithOpenApi();

        group.MapDelete("/{id}", async (IMediator mediator, Guid id) =>
        {
            var command = DeleteOrderCommand.Create(id);
            return await mediator.Send(command);
        })
        .WithName("DeleteOrder")
        .WithOpenApi();
    }
}
