using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Customer(Guid Id, Guid By, DateTime At, CRUD Action, bool IsDeleted,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    DateTime DateOfBirth,
    IEnumerable<Guid> Addresses,
    IEnumerable<Guid> Cards,
    IEnumerable<Guid> Orders
) : BaseEntity(Id, By, At, Action, IsDeleted);
