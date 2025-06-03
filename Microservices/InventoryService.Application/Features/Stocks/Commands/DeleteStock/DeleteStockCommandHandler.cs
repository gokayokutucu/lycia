using InventoryService.Application.Contracts.Persistence;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record DeleteStockCommandHandler(IInventoryRepository inventoryRepository)
    : IRequestHandler<DeleteStockCommand, DeleteStockCommandResult>
{
    public async Task<DeleteStockCommandResult> Handle(DeleteStockCommand request, CancellationToken cancellationToken)
    {
        var success = await inventoryRepository.DeleteAsync(request.StockId, cancellationToken);
        return DeleteStockCommandResult.Create(success);
    }
}
