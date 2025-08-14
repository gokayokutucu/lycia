using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Stock : BaseEntity
{
    public Guid StockId { get; init; }
    public Product Product { get; init; } = null!;
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}