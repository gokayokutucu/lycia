// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
using Lycia.Scheduling;
using Lycia.Scheduling.Abstractions;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using Sample.Shared.Messages.Responses;
using Sample.Shared.SagaStates;
using Sample.Shared.Services;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class PaymentSagaHandler(IScheduler scheduler, IMessageSerializer serializer) :
    CoordinatedSagaHandler<ProcessPaymentCommand, CreateOrderSagaData>
{
    public override async Task HandleAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        // Simulate payment process
        var paymentSucceeded = PaymentService.SimulatePayment(false);

        if (!paymentSucceeded)
        {
            // Schedule a retry via Scheduler (end-to-end Scheduling test)
            var retryCommand = new ProcessPaymentCommand
            {
                OrderId = message.OrderId,
                ParentMessageId = message.MessageId
            };

            // Build serialization context for the message type
            var ctxPair = serializer.CreateContextFor(typeof(ProcessPaymentCommand), schemaId: null, schemaVersion: null);
            // Serialize using (message, ctx) → (body, headers)
            var (body, transportHeaders) = serializer.Serialize(retryCommand, ctxPair.Ctx);

            // Merge/override headers for storage; ensure lycia-type=command so SchedulerLoop will Send<T>
            var storedHeaders = CreateStoredHeaders(transportHeaders);

            await scheduler.ScheduleAsync(new ScheduleRequest
            (
                applicationId : message.ApplicationId,
                messageType   : typeof(ProcessPaymentCommand),
                payload       : body,
                dueTime       : DateTimeOffset.UtcNow.AddSeconds(5),
                correlationId : message.CorrelationId,
                messageId     : Guid.NewGuid(),
                headers       : storedHeaders
            ), cancellationToken);

            // Mark step as failed to trigger compensation chain now
            await Context.MarkAsFailed<ProcessPaymentCommand>(cancellationToken);
            return;
        }

        // Pivot step: no compensation after this point, only retry
        Context.Data.PaymentIrreversible = true;

        // Continue
        await Context.Publish(new PaymentProcessedEvent
        {
            OrderId = message.OrderId,
            ParentMessageId = message.MessageId
        }, cancellationToken);
        await Context.MarkAsComplete<ProcessPaymentCommand>();
    }

    private static Dictionary<string, object> CreateStoredHeaders(IReadOnlyDictionary<string, object?> transportHeaders)
    {
        var storedHeaders = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in transportHeaders)
        {
            if (kv.Value != null) storedHeaders[kv.Key] = kv.Value;
        }
        storedHeaders["lycia-type"] = "command";
        return storedHeaders;
    }

    public override async Task CompensateAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        // No business compensation required, but need to bubble up
        await Context.CompensateAndBubbleUp<ProcessPaymentCommand>(cancellationToken);
    }
}