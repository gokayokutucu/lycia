using System;

namespace Sample.Order.NetFramework481.Domain.Common;

/// <summary>
/// Base class for all domain entities.
/// Provides common properties like Id, CreatedAt, UpdatedAt.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Unique identifier of the entity.
    /// </summary>
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Timestamp when the entity was created.
    /// </summary>
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the entity was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>
    /// Marks the entity as updated with current timestamp.
    /// </summary>
    protected void MarkAsUpdated() => UpdatedAt = DateTime.UtcNow;
}
