namespace InventoryService.Domain.Entities;

public sealed record Stock
{
    public Guid StockId { get; init; }
    public Guid ProductId { get; init; }
    public int Quantity { get; init; } = 0;

    public static Stock Create(Guid stockId, Guid productId, int quantity) 
        => new Stock
        {
            StockId = stockId,
            ProductId = productId,
            Quantity = quantity
        };
}
