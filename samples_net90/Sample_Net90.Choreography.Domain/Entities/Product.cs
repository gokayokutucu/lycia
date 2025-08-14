using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Product : BaseEntity
{
    public Guid ProductId { get; init; }
    public Stock Stock { get; init; } = null!;
    //public Guid StockId { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }
    public decimal Price { get; init; }
    public Currency Currency { get; init; }
}