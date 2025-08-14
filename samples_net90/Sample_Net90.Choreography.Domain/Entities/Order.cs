namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Order : BaseEntity
{
    public Guid OrderId { get; init; }
    public Customer Customer { get; init; } = null!;
    public Guid CustomerId { get; init; }
    public IEnumerable<CartItem> Products { get; init; }
    public Address DeliveryAddress { get; init; } = null!;
    public Guid DeliveryAddressId { get; init; }
    public Address BillingAddress { get; init; } = null!;
    public Guid BillingAddressId { get; init; }
}