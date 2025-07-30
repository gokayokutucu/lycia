using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Product(
    Guid Id,
        Guid By,
        DateTime At,
        CRUD Action,
        bool IsDeleted,
    Guid StockId,
    string Name,
    string Description,
    decimal Price,
    Currency Currency
) : BaseEntity(Id, By, At, Action, IsDeleted);