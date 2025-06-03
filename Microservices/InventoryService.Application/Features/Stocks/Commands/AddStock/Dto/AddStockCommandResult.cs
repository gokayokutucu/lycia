namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record AddStockCommandResult
{
    public bool Success { get; init; }
    public static AddStockCommandResult Create(bool success) =>
        new AddStockCommandResult
        {
            Success = success,
        };
}
