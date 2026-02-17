using System;
using System.Collections.Generic;
using Lycia.Saga.Messaging;
using Shared.Contracts.Dtos;

namespace Shared.Contracts.Events.Delivery;

public sealed class ShipmentScheduledFailedEvent : FailedEventBase
{
    public ShipmentScheduledFailedEvent(string reason) : base(reason)
    {
    }

    public Guid OrderId { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
}
