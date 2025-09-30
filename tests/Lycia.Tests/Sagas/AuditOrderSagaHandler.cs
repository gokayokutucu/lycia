// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Messaging.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;

namespace Lycia.Tests.Sagas;

public class AuditOrderSagaHandler : CoordinatedSagaHandler<OrderCreatedEvent, CreateOrderSagaData>
{
    public override Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default)
    {
        try
        {
            return Context.MarkAsComplete<OrderCreatedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            return Context.MarkAsFailed<OrderCreatedEvent>(cancellationToken);
        }
    }
}