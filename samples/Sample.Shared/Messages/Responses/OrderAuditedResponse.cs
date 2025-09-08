// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;
using Sample.Shared.Messages.Events;

namespace Sample.Shared.Messages.Responses;

public class OrderAuditedResponse : ResponseBase<OrderCreatedEvent>
{
    public Guid OrderId { get; set; }
}