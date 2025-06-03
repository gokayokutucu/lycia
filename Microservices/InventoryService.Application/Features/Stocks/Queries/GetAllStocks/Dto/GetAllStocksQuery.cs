using MediatR;

namespace InventoryService.Application.Features.Stocks.Queries;

public sealed record GetAllStocksQuery : IRequest<GetAllStocksQueryResult>
{
    public static GetAllStocksQuery Create() => new GetAllStocksQuery();
}
