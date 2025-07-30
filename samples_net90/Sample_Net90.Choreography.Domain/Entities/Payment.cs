using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Payment(
    Guid Id,
    Guid By,
    DateTime At,
    CRUD Action,
    bool IsDeleted
) : BaseEntity(Id, By, At, Action, IsDeleted);

