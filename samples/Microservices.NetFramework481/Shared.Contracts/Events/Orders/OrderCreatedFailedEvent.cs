using System;
using Lycia.Saga.Messaging;

namespace Shared.Contracts.Events.Orders;

public sealed class OrderCreatedFailedEvent : FailedEventBase
{
    public OrderCreatedFailedEvent(string reason) : base(reason)
    {

    }
    public Guid OrderId { get; set; }
}
