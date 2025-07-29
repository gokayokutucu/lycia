using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Domain.Entities;

public class BaseEntity
{
    public Guid Id { get; set; }
    public Guid By { get; init; }
    public DateTime At { get; init; }
    public CRUD Action { get; init; }
    public bool IsDeleted { get; init; }
}
