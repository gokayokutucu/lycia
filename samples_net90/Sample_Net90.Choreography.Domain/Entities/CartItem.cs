namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class CartItem : BaseEntity
{
    public Guid CartItemId { get; init; }
    public Order Order { get; init; } = null!;
    public Guid OrderId { get; init; }
    public Product Product { get; init; } = null!;
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}