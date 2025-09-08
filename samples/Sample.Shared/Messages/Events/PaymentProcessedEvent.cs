// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Sample.Shared.Messages.Events;

public class PaymentProcessedEvent : EventBase
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public decimal PaymentId { get; set; }
}