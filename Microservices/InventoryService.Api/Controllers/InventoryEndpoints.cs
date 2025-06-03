using InventoryService.Application.Features.Stocks.Commands;
using InventoryService.Application.Features.Stocks.Queries;
using InventoryService.Domain.Entities;
using MediatR;
namespace InventoryService.Api.Controllers;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints (this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/stock").WithTags(nameof(Stock));

        group.MapGet("/", async (IMediator mediator) =>
        {
            var query = GetAllStocksQuery.Create();
            return await mediator.Send(query);
        })
        .WithName("GetAllStocks")
        .WithOpenApi();

        group.MapGet("/{id}", async (IMediator mediator, Guid id) =>
        {
            var query = GetStockByIdQuery.Create(id);
            return await mediator.Send(query);
        })
        .WithName("GetStockById")
        .WithOpenApi();

        group.MapPut("/{id}", async (IMediator mediator, Guid id, Stock Stock) =>
        {
            var command = UpdateStockCommand.Create(Stock with { StockId = id });
            return await mediator.Send(command);
        })
        .WithName("UpdateStock")
        .WithOpenApi();

        group.MapPost("/", async (IMediator mediator, Stock Stock) =>
        {
            var command = AddStockCommand.Create(Stock);
            return await mediator.Send(command);
        })
        .WithName("CreateStock")
        .WithOpenApi();

        group.MapDelete("/{id}", async (IMediator mediator, Guid id) =>
        {
            var command = DeleteStockCommand.Create(id);
            return await mediator.Send(command);
        })
        .WithName("DeleteStock")
        .WithOpenApi();
    }
}
