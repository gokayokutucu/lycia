using InventoryService.Application.Contracts.Persistence;
using MediatR;

namespace InventoryService.Application.Features.Stocks.Queries;

public sealed record GetAllStocksQueryHandler (IInventoryRepository inventoryRepository)
    : IRequestHandler<GetAllStocksQuery, GetAllStocksQueryResult>
{
    public async Task<GetAllStocksQueryResult> Handle(GetAllStocksQuery request, CancellationToken cancellationToken)
    {
        var stocks = await inventoryRepository.GetAllAsync(cancellationToken);
        return GetAllStocksQueryResult.Create(stocks);
    }
}
