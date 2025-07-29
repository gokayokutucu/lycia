using MediatR;
using Sample_Net90.Choreography.Application.Order.Commands.Create;

namespace Sample_Net90.Choreography.Api.EndPoints;

public static class OrdersEndpoints
{
    public static void MapOrdersEndpoints(this WebApplication app)
    {
        var orders = new List<Order>
        {
            new Order { Id = 1, Description = "First Order" },
            new Order { Id = 2, Description = "Second Order" }
        };

        app.MapGet("/orders", () => orders)
        //.WithName("GetOrders")
        ;

        app.MapGet("/orders/{id:int}", (int id) =>
        {
            var order = orders.FirstOrDefault(o => o.Id == id);
            return order is not null ? Results.Ok(order) : Results.NotFound();
        })
        //.WithName("GetOrderById")
        ;

        app.MapPost("/orders", async (IMediator mediator,  CreateOrderCommand order) =>
        {
            var id = await mediator.Send(order);
            return Results.Created($"/orders/{id}", order);
        })
        //.WithName("CreateOrder")
        ;

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