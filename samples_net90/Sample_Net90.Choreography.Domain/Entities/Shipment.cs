using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed class Shipment : BaseEntity
{
    public Guid ShipmentId { get; init; }
    public Order Order { get; init; } = null!;
    public Guid OrderId { get; init; }
    public Address DeliveryAddress { get; init; } = null!;
    public Guid DeliveryAddressId { get; init; }
    public Address BillingAddress { get; init; } = null!;
    public Guid BillingAddressId { get; init; }
    public ShipmentStatus Status { get; init; }
}