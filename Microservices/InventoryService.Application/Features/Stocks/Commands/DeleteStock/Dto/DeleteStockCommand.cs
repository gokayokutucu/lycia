using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record DeleteStockCommand : IRequest<DeleteStockCommandResult>
{
    public Guid StockId { get; init; }
    public static DeleteStockCommand Create(Guid stockId) =>
        new DeleteStockCommand
        {
            StockId = stockId,
        };
}
