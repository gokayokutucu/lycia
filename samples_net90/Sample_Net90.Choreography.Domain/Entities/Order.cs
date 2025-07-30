using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Order(
    Guid Id,
        Guid By,
        DateTime At,
        CRUD Action,
        bool IsDeleted,
    Guid CustomerId,
    IEnumerable<Guid> Products
) : BaseEntity(Id, By, At, Action, IsDeleted);