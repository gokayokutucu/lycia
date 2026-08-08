// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lycia.Outbox;

/// <inheritdoc cref="IOutboxDispatcher" />
public class OutboxDispatcher(IOutboxStore outboxStore, IEventBus eventBus, ILogger<OutboxDispatcher> logger)
    : IOutboxDispatcher
{
    private static readonly MethodInfo PublishMethodDefinition = typeof(IEventBus)
        .GetMethod(nameof(IEventBus.Publish))!;

    /// <inheritdoc />
    public async Task<OutboxDispatchResult> DispatchPendingBatchAsync(int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var claimed = await outboxStore.ClaimPendingBatchAsync(maxCount, cancellationToken);

        var published = 0;
        var confirmationUnknown = 0;
        var failed = 0;

        foreach (var message in claimed)
        {
            var outcome = await DispatchOneAsync(message, cancellationToken);
            switch (outcome)
            {
                case OutboxMessageStatus.Published: published++; break;
                case OutboxMessageStatus.ConfirmationUnknown: confirmationUnknown++; break;
                default: failed++; break;
            }
        }

        return new OutboxDispatchResult
        {
            Claimed = claimed.Count,
            Published = published,
            ConfirmationUnknown = confirmationUnknown,
            Failed = failed
        };
    }

    private async Task<OutboxMessageStatus> DispatchOneAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Local failure before anything left the process: the message never reached the transport,
        // so it's safe to mark Failed rather than ConfirmationUnknown.
        object? deserialized;
        Type? messageType;
        try
        {
            messageType = Type.GetType(message.MessageTypeName);
            if (messageType == null)
                throw new InvalidOperationException($"Could not resolve outbox message type '{message.MessageTypeName}'.");

            if (!typeof(IEvent).IsAssignableFrom(messageType))
                throw new InvalidOperationException(
                    $"Outbox dispatch only supports IEvent-typed messages today; '{messageType.FullName}' is not an IEvent. " +
                    "Send/Respond routing through the Outbox is not yet implemented.");

            deserialized = JsonConvert.DeserializeObject(message.Payload, messageType);
            if (deserialized == null)
                throw new InvalidOperationException($"Outbox payload for message {message.MessageId} deserialized to null.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox message {MessageId} could not be prepared for dispatch; marking Failed.", message.MessageId);
            await outboxStore.MarkFailedAsync(message.MessageId,
                new SagaStepFailureInfo("Outbox payload could not be prepared for dispatch", ex.GetType().Name, ex.ToString()),
                cancellationToken);
            return OutboxMessageStatus.Failed;
        }

        await outboxStore.MarkPublishingAsync(message.MessageId, cancellationToken);

        try
        {
            var publish = PublishMethodDefinition.MakeGenericMethod(messageType);
            var task = (Task)publish.Invoke(eventBus, [deserialized, null, message.SagaId, cancellationToken])!;
            await task;

            // No broker-level publisher-confirms wiring exists yet: this only reflects that the
            // transport call completed without throwing, not a verified broker acknowledgment.
            await outboxStore.MarkPublishedAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.Published;
        }
        catch (Exception ex)
        {
            // The publish attempt reached the transport; we cannot know whether the broker received
            // it before the failure, so this is ConfirmationUnknown, not a definite Failed.
            logger.LogWarning(ex, "Outbox message {MessageId} publish attempt did not confirm success; marking ConfirmationUnknown.", message.MessageId);
            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
    }
}
