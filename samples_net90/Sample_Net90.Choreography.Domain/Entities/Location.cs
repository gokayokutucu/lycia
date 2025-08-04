using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record StockLocation(Guid Id, Guid By, DateTime At, CRUD Action, bool IsDeleted,
    string Warehouse,
    string Zone,
    string Block,
    int Shelve
) : BaseEntity(Id, By, At, Action, IsDeleted);
