namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record UpdateStockCommandResult
{
    public bool Success { get; init; }
    public static UpdateStockCommandResult Create(bool success) 
        => new UpdateStockCommandResult
        {
            Success = success
        };
}
