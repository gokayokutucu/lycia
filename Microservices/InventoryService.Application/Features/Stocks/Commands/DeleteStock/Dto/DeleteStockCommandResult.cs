namespace InventoryService.Application.Features.Stocks.Commands;

public sealed record DeleteStockCommandResult
{
    public bool Success { get; init; }
    public static DeleteStockCommandResult Create(bool success) => new DeleteStockCommandResult
    {
        Success = success,
    };
}
