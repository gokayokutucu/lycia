using InventoryService.Application.Contracts.Persistence;
using InventoryService.Application.Features.Stocks.Queries;
using MediatR;

namespace InventoryService.Application.Features.Stocks;

public sealed record GetStockByIdQueryHandler(IInventoryRepository inventoryRepository)
    : IRequestHandler<GetStockByIdQuery, GetStockByIdQueryResult>
{
    public async Task<GetStockByIdQueryResult> Handle(GetStockByIdQuery request, CancellationToken cancellationToken)
    {
        var stock = await inventoryRepository.GetByIdAsync(request.StockId, cancellationToken);
        return GetStockByIdQueryResult.Create(stock);
    }
}