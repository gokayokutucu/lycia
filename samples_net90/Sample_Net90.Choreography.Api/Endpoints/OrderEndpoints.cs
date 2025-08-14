using MediatR;
using Microsoft.AspNetCore.Mvc;
using Sample_Net90.Choreography.Application.Order.Commands.Create;
using Sample_Net90.Choreography.Application.Payment.Commands.Process;

namespace Sample_Net90.Choreography.Api.EndPoints;

public static class OrdersEndpoints
{
    public static void MapOrdersEndpoints(this WebApplication app)
    {
        app.MapPost("/orders", async (IMediator mediator, [FromBody] CreateOrderCommand command) =>
        {
            var orderId = await mediator.Send(command);
            return Results.Ok(orderId);
        })
        .WithName("CreateOrder")
        .WithOpenApi();

        app.MapPost("/orders/{orderId:Guid}/payment", async (IMediator mediator, [FromRoute] Guid orderId, [FromBody] ProcessPaymentCommand command) =>
        {
            command.OrderId = orderId;
            var paymentId = await mediator.Send(command);
            return Results.Ok(paymentId);
        })
        .WithName("Payment")
        .WithOpenApi();
    }
}