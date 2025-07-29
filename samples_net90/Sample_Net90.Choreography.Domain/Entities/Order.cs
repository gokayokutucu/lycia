namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Order : BaseEntity
{
    public Guid CustomerId { get; init; }

    public IEnumerable<Guid> Products { get; init; }
}
