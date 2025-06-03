using MediatR;

namespace InventoryService.Application.Features.Stocks.Queries;

public sealed record GetStockByIdQuery : IRequest<GetStockByIdQueryResult>
{
    public Guid StockId { get; init; }
    public static GetStockByIdQuery Create(Guid stockId) 
        => new GetStockByIdQuery
        {
            StockId = stockId
        };
}
