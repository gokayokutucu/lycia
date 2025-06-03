using InventoryService.Application.Contracts.Persistence;
using InventoryService.Domain.Entities;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record AddStockCommandHandler(IInventoryRepository inventoryRepository)
    : IRequestHandler<AddStockCommand, AddStockCommandResult>
{
    public async Task<AddStockCommandResult> Handle(AddStockCommand request, CancellationToken cancellationToken)
    {
        var stock = Stock.Create(Guid.Empty, request.ProductId, request.Quantity);
        var success = await inventoryRepository.AddAsync(stock, cancellationToken);
        return AddStockCommandResult.Create(success);
    }
}
