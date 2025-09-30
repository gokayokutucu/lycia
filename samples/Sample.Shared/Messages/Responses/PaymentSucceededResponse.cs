// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging;
using Sample.Shared.Messages.Commands;

namespace Sample.Shared.Messages.Responses;

public class PaymentSucceededResponse : ResponseBase<ProcessPaymentCommand>
{
    public Guid OrderId { get; set; }
}