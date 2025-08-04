using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Shipment(Guid Id, Guid By, DateTime At, CRUD Action, bool IsDeleted,
    Guid OrderId,
    Guid DeliveryAddressId,
    Guid BillingAddressId,
    ShipmentStatus Status
) : BaseEntity(Id, By, At, Action, IsDeleted);