using System;
using Lycia.Saga.Messaging;

namespace Shared.Contracts.Events.Notification;

/// <summary>
/// Event published when customer notification fails.
/// Saga completes but logs warning (order is still valid).
/// </summary>
public sealed class CustomerNotifiedFailedEvent : FailedEventBase
{
    public CustomerNotifiedFailedEvent(string reason) : base(reason)
    {

    }

    public Guid OrderId { get; set; }
}
