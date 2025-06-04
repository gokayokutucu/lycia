using MediatR;
using Lycia.Saga.Abstractions; // Required for IEventBus
using Sample.Shared.Messages.Events; // Required for OrderCreationInitiatedEvent
using OrderService.Api.Dtos; // Required for CreateOrderSagaRequestDto
using OrderService.Application.Features.Orders.Commands;
using OrderService.Application.Features.Orders.Queries;
using OrderService.Domain.Entities;
using System; // Required for Guid

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
            // This endpoint might need to be re-evaluated in an event-driven architecture.
            // For now, keeping it as is, but typically updates would also go through events/commands.
            var command = UpdateOrderCommand.Create(order with { OrderId = id });
            return await mediator.Send(command);
        })
        .WithName("UpdateOrder")
        .WithOpenApi();

        // Modified to initiate a saga
        group.MapPost("/", async (IEventBus eventBus, CreateOrderSagaRequestDto createOrderRequest) =>
        {
            var orderId = Guid.NewGuid();
            // In a real app, UserId would likely come from authentication context
            var userId = Guid.NewGuid(); // Placeholder

            var orderEvent = new OrderCreationInitiatedEvent
            {
                OrderId = orderId,
                UserId = userId,
                ProductId = createOrderRequest.ProductId,
                Quantity = createOrderRequest.Quantity,
                TotalPrice = createOrderRequest.TotalPrice
            };

            // Assuming IEventBus has a PublishAsync method.
            // If Lycia's IEventBus has a different signature (e.g. non-async Publish or specific parameters), adjust this.
            await eventBus.Publish(orderEvent); // Corrected based on common Lycia IEventBus pattern (often synchronous Publish)

            return Results.Accepted($"/api/shopping-cart/{orderId}", new { OrderId = orderId, Status = "Processing" });
        })
        .WithName("CreateOrder")
        .WithOpenApi();

        group.MapDelete("/{id}", async (IMediator mediator, Guid id) =>
        {
            // Deletes might also be handled via events in some patterns.
            var command = DeleteOrderCommand.Create(id);
            return await mediator.Send(command);
        })
        .WithName("DeleteOrder")
        .WithOpenApi();
    }
}
