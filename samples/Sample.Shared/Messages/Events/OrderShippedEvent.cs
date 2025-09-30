// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging;

namespace Sample.Shared.Messages.Events;

public class OrderShippedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public DateTime ShippedAt { get; set; } = DateTime.UtcNow;
    public Guid ShipmentTrackId { get; set; }
}