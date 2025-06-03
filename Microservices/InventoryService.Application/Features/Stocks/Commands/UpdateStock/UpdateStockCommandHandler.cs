using InventoryService.Application.Contracts.Persistence;
using InventoryService.Domain.Entities;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record UpdateStockCommandHandler(IInventoryRepository inventoryRepository)
    : IRequestHandler<UpdateStockCommand, UpdateStockCommandResult>
{
    public async Task<UpdateStockCommandResult> Handle(UpdateStockCommand request, CancellationToken cancellationToken)
    {
        var stock = Stock.Create(request.StockId, request.ProductId, request.Quantity);
        var success = await inventoryRepository.UpdateAsync(stock, cancellationToken);
        return UpdateStockCommandResult.Create(success);
    }
}
