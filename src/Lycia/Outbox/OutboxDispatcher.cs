// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Diagnostics;
using System.Reflection;
using Lycia.Common.SagaSteps;
using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lycia.Outbox;

/// <inheritdoc cref="IOutboxDispatcher" />
public class OutboxDispatcher(IOutboxStore outboxStore, IEventBus eventBus, IMessageSerializer serializer,
    LyciaActivitySourceHolder activitySourceHolder, ILogger<OutboxDispatcher> logger)
    : IOutboxDispatcher
{
    /// <inheritdoc />
    public async Task<OutboxDispatchResult> DispatchPendingBatchAsync(int maxCount = 50,
        CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
    {
        var claimed = await outboxStore.ClaimPendingBatchAsync(maxCount, cancellationToken, maxAttempts,
            recoveryTimeout);

        var published = 0;
        var confirmationUnknown = 0;
        var failed = 0;
        var abandoned = 0;

        foreach (var message in claimed)
        {
            var outcome = await DispatchOneAsync(message, maxAttempts, cancellationToken);
            switch (outcome)
            {
                case OutboxMessageStatus.Published: published++; break;
                case OutboxMessageStatus.ConfirmationUnknown: confirmationUnknown++; break;
                case OutboxMessageStatus.Abandoned: abandoned++; break;
                default: failed++; break;
            }
        }

        return new OutboxDispatchResult
        {
            Claimed = claimed.Count,
            Published = published,
            ConfirmationUnknown = confirmationUnknown,
            Failed = failed,
            Abandoned = abandoned
        };
    }

    private async Task<OutboxMessageStatus> DispatchOneAsync(OutboxMessage message, int maxAttempts,
        CancellationToken cancellationToken)
    {
        // Local failure before anything left the process: the message never reached the transport,
        // so it's safe to mark Failed rather than ConfirmationUnknown.
        OutboxEnvelope envelope;
        object deserialized;
        Type messageType;
        try
        {
            envelope = JsonConvert.DeserializeObject<OutboxEnvelope>(message.Payload)
                ?? throw new InvalidOperationException($"Outbox envelope {message.MessageId} deserialized to null.");
            if (envelope.Version != 1 || envelope.OutboxId != message.MessageId || envelope.MessageId != message.MessageId)
                throw new InvalidOperationException($"Outbox envelope {message.MessageId} has inconsistent identity or version.");
            messageType = ResolveType(envelope.MessageType, "outgoing message");
            deserialized = Deserialize(envelope.Body, envelope.Headers, messageType);
            if (deserialized is not IMessage typed || typed.MessageId != message.MessageId)
                throw new InvalidOperationException($"Outbox payload MessageId differs from stored MessageId '{message.MessageId}'.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox message {MessageId} could not be prepared for dispatch; marking Failed.", message.MessageId);
            await outboxStore.MarkFailedAsync(message.MessageId,
                new SagaStepFailureInfo("Outbox payload could not be prepared for dispatch", ex.GetType().Name, ex.ToString()),
                cancellationToken);
            return OutboxMessageStatus.Failed;
        }

        // The row was only claimable because RetryCount was still below maxAttempts, so this attempt is
        // attempt number RetryCount + 1. When that is the last permitted attempt, any outcome other than a
        // positive confirmation must become the terminal Abandoned state: leaving it at
        // ConfirmationUnknown with the attempt count at the cap would make it invisible to every future
        // claim query, with no terminal status and no recorded reason — silently dropping the message.
        var isFinalAttempt = message.RetryCount + 1 >= maxAttempts;
        await outboxStore.MarkPublishingAsync(message.MessageId, cancellationToken);

        try
        {
            var confirmed = eventBus is IConfirmedEventBus;
            await DispatchSemanticAsync(envelope, messageType, deserialized, cancellationToken);
            if (confirmed)
            {
                await outboxStore.MarkPublishedAsync(message.MessageId, cancellationToken);
                return OutboxMessageStatus.Published;
            }

            if (isFinalAttempt)
                return await AbandonAsync(message, maxAttempts,
                    "Outbox dispatch attempts exhausted without a broker confirmation. The transport accepted " +
                    "the publish but cannot confirm it, so the delivery outcome is unknown.",
                    null, cancellationToken);

            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A transport call may already have reached the broker. Persist the ambiguous outcome,
            // then honor shutdown cancellation so the hosted worker stops promptly. Even here the final
            // attempt must terminalize, otherwise a shutdown landing on the last attempt strands the row.
            if (isFinalAttempt)
                await AbandonAsync(message, maxAttempts,
                    "Outbox dispatch attempts exhausted; the final attempt was cancelled during shutdown, " +
                    "so the delivery outcome is unknown.", null, CancellationToken.None);
            else
                await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // The publish attempt reached the transport; we cannot know whether the broker received
            // it before the failure, so this is ConfirmationUnknown, not a definite Failed.
            if (isFinalAttempt)
                return await AbandonAsync(message, maxAttempts,
                    "Outbox dispatch attempts exhausted; the last publish attempt threw, so the delivery " +
                    "outcome is unknown.", ex, cancellationToken);

            logger.LogWarning(ex, "Outbox message {MessageId} publish attempt did not confirm success; marking ConfirmationUnknown.", message.MessageId);
            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
    }

    private async Task<OutboxMessageStatus> AbandonAsync(OutboxMessage message, int maxAttempts, string reason,
        Exception? exception, CancellationToken cancellationToken)
    {
        // Warning, not Information: an abandoned message is unshipped business intent that needs a human.
        logger.LogWarning(exception,
            "Outbox message {MessageId} (saga {SagaId}) abandoned after {Attempts} of {MaxAttempts} dispatch attempts: {Reason} " +
            "It will not be dispatched again automatically and requires operator action.",
            message.MessageId, message.SagaId, message.RetryCount + 1, maxAttempts, reason);

        await outboxStore.MarkAbandonedAsync(message.MessageId,
            new SagaStepFailureInfo(reason, exception?.GetType().Name ?? nameof(OutboxMessageStatus.Abandoned),
                exception?.ToString()), cancellationToken);
        return OutboxMessageStatus.Abandoned;
    }

    private async Task DispatchSemanticAsync(OutboxEnvelope envelope, Type messageType, object message,
        CancellationToken cancellationToken)
    {
        // Restore the trace context captured at envelope creation time (see
        // OutboxOutgoingMessagePipeline.CaptureAsync) so the transport's own Activity.Current-based
        // header injection continues the original caller's trace instead of starting a disconnected
        // one from whatever happens to be current on this background dispatch loop.
        var parentContext = LyciaTracePropagation.Extract(envelope.Headers);
        using var activity = parentContext != default
            ? activitySourceHolder.Source.StartActivity($"Outbox.{envelope.Operation}", ActivityKind.Producer, parentContext)
            : null;

        var target = eventBus is IConfirmedEventBus ? typeof(IConfirmedEventBus) : typeof(IEventBus);
        var instance = eventBus;
        var handlerType = string.IsNullOrWhiteSpace(envelope.HandlerType)
            ? null : ResolveType(envelope.HandlerType!, "handler");
        switch (envelope.Operation)
        {
            case OutboxOperationKind.Send:
                await InvokeGeneric(target, instance, eventBus is IConfirmedEventBus ? nameof(IConfirmedEventBus.SendConfirmed) : nameof(IEventBus.Send),
                    [messageType], [message, handlerType, envelope.SagaId, cancellationToken]);
                return;
            case OutboxOperationKind.Publish:
                await InvokeGeneric(target, instance, eventBus is IConfirmedEventBus ? nameof(IConfirmedEventBus.PublishConfirmed) : nameof(IEventBus.Publish),
                    [messageType], [message, handlerType, envelope.SagaId, cancellationToken]);
                return;
            case OutboxOperationKind.Respond:
                if (envelope.RequestBody == null || envelope.RequestHeaders == null ||
                    string.IsNullOrWhiteSpace(envelope.RequestType))
                    throw new InvalidOperationException($"Response envelope '{envelope.OutboxId}' has no durable request.");
                var requestType = ResolveType(envelope.RequestType!, "response request");
                var request = Deserialize(envelope.RequestBody, envelope.RequestHeaders, requestType);
                await InvokeGeneric(target, instance, eventBus is IConfirmedEventBus ? nameof(IConfirmedEventBus.RespondConfirmed) : nameof(IEventBus.Respond),
                    [requestType, messageType], [request, message, handlerType, envelope.SagaId, cancellationToken]);
                return;
            default:
                throw new InvalidOperationException($"Unsupported Outbox operation '{envelope.Operation}'.");
        }
    }

    private object Deserialize(byte[] body, IReadOnlyDictionary<string, object?> headers, Type type)
    {
        var (_, context) = serializer.CreateContextFor(type);
        return serializer.Deserialize(body, headers, context);
    }

    private static Type ResolveType(string typeName, string role) => Type.GetType(typeName, false)
        ?? throw new InvalidOperationException($"Could not resolve Outbox {role} type '{typeName}'.");

    private static async Task InvokeGeneric(Type contract, object instance, string methodName, Type[] genericArguments,
        object?[] arguments)
    {
        var method = contract.GetMethods().Single(candidate => candidate.Name == methodName &&
            candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length == genericArguments.Length);
        try
        {
            await ((Task)(method.MakeGenericMethod(genericArguments).Invoke(instance, arguments)
                ?? throw new InvalidOperationException($"Event bus method '{methodName}' returned null.")));
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }
}
