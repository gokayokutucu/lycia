using Sample_Net90.Choreography.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sample_Net90.Choreography.Domain.Entities;

public abstract class BaseEntity
{
    [NotMapped]
    public Guid AuditId { get; init; }
    [NotMapped]
    public Guid By { get; init; }
    [NotMapped]
    public DateTime At { get; init; }
    [NotMapped]
    public CRUD Action { get; init; }
    [NotMapped]
    public string Detail { get; init; }
}