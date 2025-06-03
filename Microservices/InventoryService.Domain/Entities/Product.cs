namespace InventoryService.Domain.Entities;

public sealed record Product
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; } = 0.0m;

    public static Product Create(Guid productId, string name, string description, decimal price) 
        => new Product
        {
            ProductId = productId,
            Name = name,
            Description = description,
            Price = price
        };
}
