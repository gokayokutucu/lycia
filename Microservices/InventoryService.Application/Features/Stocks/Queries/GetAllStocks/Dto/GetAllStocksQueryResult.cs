using InventoryService.Domain.Entities;

namespace InventoryService.Application.Features.Stocks.Queries;

public sealed record GetAllStocksQueryResult
{
    public IEnumerable<Stock> Stocks { get; init; }
    public static GetAllStocksQueryResult Create(IEnumerable<Stock> stocks) 
        => new GetAllStocksQueryResult
        {
            Stocks = stocks
        };
}
