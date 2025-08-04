using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Stock(Guid Id, Guid By, DateTime At, CRUD Action, bool IsDeleted,
    Guid OrderId,
    Guid ProductId,
    int Quantity
) : BaseEntity(Id, By, At, Action, IsDeleted);