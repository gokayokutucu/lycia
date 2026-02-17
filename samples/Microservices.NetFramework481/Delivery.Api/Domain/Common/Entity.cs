using System;

namespace Sample.Delivery.NetFramework481.Domain.Common;

/// <summary>
/// Base entity class with Id property.
/// </summary>
public abstract class Entity
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; internal set; } = Guid.NewGuid();
}
