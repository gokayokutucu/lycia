using InventoryService.Domain.Entities;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record AddStockCommand : IRequest<AddStockCommandResult>
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public static AddStockCommand Create(Stock stock) =>
        new AddStockCommand
        {
            ProductId = stock.ProductId,
            Quantity = stock.Quantity,
        };
}
