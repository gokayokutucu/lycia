namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Stock : BaseEntity
{
    public Guid ProductId { get; init; }
    public Guid LocationId { get; init; }

    public int Quantity { get; init; }
}
