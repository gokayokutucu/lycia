using System;

namespace Sample.Notification.NetFramework481.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; internal set; } = Guid.NewGuid();
}
