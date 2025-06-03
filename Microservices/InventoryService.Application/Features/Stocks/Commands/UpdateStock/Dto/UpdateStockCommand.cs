using InventoryService.Domain.Entities;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record UpdateStockCommand: IRequest<UpdateStockCommandResult>
{
    public Guid StockId { get; init; }
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }

    public static UpdateStockCommand Create(Stock stock) 
        => new UpdateStockCommand
        {
            StockId = stock.StockId,
            ProductId = stock.ProductId,
            Quantity = stock.Quantity
        };
}
