using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Address(
    Guid Id,
        Guid By,
        DateTime At,
        CRUD Action,
        bool IsDeleted,
    Guid CustomerId,
    string Country,
    string State,
    string City,
    string Street,
    string Building,
    int Floor,
    string Apartment,
    string PostalCode
) : BaseEntity(Id, By, At, Action, IsDeleted);

