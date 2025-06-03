using InventoryService.Domain.Entities;

namespace InventoryService.Application.Features.Stocks.Queries;

public sealed record GetStockByIdQueryResult
{
    public Stock Stock { get; init; }
    public static GetStockByIdQueryResult Create(Stock stockEntity) 
        => new GetStockByIdQueryResult
        {
            Stock = stockEntity
        };
}
