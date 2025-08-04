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
            var id = await mediator.Send(command);
            return Results.Ok;
        });

        app.MapPost("/orders/{id:guid}/payment", async (IMediator mediator, ProcessPaymentCommand command) =>
        {
            var id = await mediator.Send(command);
            return Results.Ok;
        });
    }
}