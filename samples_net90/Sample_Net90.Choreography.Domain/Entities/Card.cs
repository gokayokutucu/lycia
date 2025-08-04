using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public sealed record Card(Guid Id,Guid By,DateTime At,CRUD Action,bool IsDeleted,
    Guid CustomerId,
    string CardNumber,
    string CardHolderName,
    DateTime ExpirationDate,
    string CVV
) : BaseEntity(Id, By, At, Action, IsDeleted);