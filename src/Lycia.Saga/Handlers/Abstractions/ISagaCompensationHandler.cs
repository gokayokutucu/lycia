// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Saga.Handlers.Abstractions;

public interface ISagaCompensationHandler<in TMessage> where TMessage : IMessage
{
    Task CompensateAsync(TMessage message, CancellationToken cancellationToken = default);
}