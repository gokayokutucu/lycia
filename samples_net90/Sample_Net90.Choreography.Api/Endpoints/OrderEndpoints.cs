using MediatR;
using Sample_Net90.Choreography.Application.Order.Commands.Create;

namespace Sample_Net90.Choreography.Api.EndPoints;

public static class OrdersEndpoints
{
    public static void MapOrdersEndpoints(this WebApplication app)
    {
        app.MapPost("/orders", async (IMediator mediator,  CreateOrderCommand command) =>
        {
            var id = await mediator.Send(command);
            return Results.Ok;
        });

        app.MapPost("/orders/{id:int}/payment", async (IMediator mediator, ProcessPaymentCommand command) =>
        {
            var id = await mediator.Send(command);
            return Results.Ok;
        });

        app.MapPut("/orders/{id:int}", (int id, Order updatedOrder) =>
        {
            var order = orders.FirstOrDefault(o => o.Id == id);
            if (order is null)
                return Results.NotFound();
            order.Description = updatedOrder.Description;
            return Results.Ok(order);
        })
        //.WithName("UpdateOrder")
        ;

        app.MapDelete("/orders/{id:int}", (int id) =>
        {
            var order = orders.FirstOrDefault(o => o.Id == id);
            if (order is null)
                return Results.NotFound();
            orders.Remove(order);
            return Results.NoContent();
        })
        //.WithName("DeleteOrder")
        ;
    }
}