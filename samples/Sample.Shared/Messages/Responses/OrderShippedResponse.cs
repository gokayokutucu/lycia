// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Responses;

public class OrderShippedResponse : ResponseBase<ShipOrderCommand>
{
    public Guid OrderId { get; set; }
}

public class ShippedOrderResponse : ResponseBase<OrderCreatedEvent>
{
    public Guid OrderId { get; set; }
}