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

        // RetryCount is the number of attempts STARTED (MarkPublishingAsync counts an attempt before the
        // transport is called), including any whose outcome was never recorded. A Pending or
        // ConfirmationUnknown row is only claimable while that count is below the cap, so it can only reach
        // this point at or above the cap as a stale Claimed/Publishing row: an attempt whose worker died
        // before recording an outcome. Whether that attempt reached the broker is unknowable, so:
        //   below the cap  - an ordinary retry;
        //   at the cap     - the last permitted attempt is in doubt. Publish once more, with the same
        //                    MessageId: a duplicate is acceptable under at-least-once, silently dropping or
        //                    assuming delivery is not;
        //   above the cap  - that recovery attempt was itself lost, so two workers have now died on this
        //                    message. Stop publishing and make it operator-visible instead of looping.
        var attemptsStarted = message.RetryCount;
        if (attemptsStarted > maxAttempts)
            return await AbandonAsync(message, attemptsStarted, maxAttempts,
                "Outbox dispatch stopped: workers stopped mid-dispatch on the final attempt and again on its " +
                "recovery attempt, so the delivery outcome is unknown.", null, cancellationToken);

        if (attemptsStarted >= maxAttempts)
            logger.LogWarning(
                "Outbox message {MessageId} (saga {SagaId}) is in doubt: a worker stopped during its last permitted " +
                "dispatch attempt ({Attempts} of {MaxAttempts}) before recording the outcome, so it may or may not " +
                "have reached the transport. Publishing it once more with the same MessageId (at-least-once); " +
                "consumers must be idempotent.",
                message.MessageId, message.SagaId, attemptsStarted, maxAttempts);

        // When this is the last attempt it decides the row's fate: a publish that throws is Abandoned, an
        // accepted-but-unconfirmed one stays ConfirmationUnknown. An unconfirming transport such as Core NATS
        // reports every successful publish that way, so abandoning it would raise an "operator action
        // required" warning for essentially every delivered message. Left at ConfirmationUnknown with the
        // count at the cap it is simply not dispatched again.
        var isFinalAttempt = attemptsStarted + 1 >= maxAttempts;
        await outboxStore.MarkPublishingAsync(message.MessageId, cancellationToken);

        try
        {
            var confirmed = SupportsConfirmation();
            await DispatchSemanticAsync(envelope, messageType, deserialized, cancellationToken);
            if (confirmed)
            {
                await outboxStore.MarkPublishedAsync(message.MessageId, cancellationToken);
                return OutboxMessageStatus.Published;
            }

            if (isFinalAttempt)
                logger.LogInformation(
                    "Outbox message {MessageId} used its last of {MaxAttempts} dispatch attempts; the transport accepted " +
                    "it but cannot confirm delivery, so it remains ConfirmationUnknown and will not be redispatched.",
                    message.MessageId, maxAttempts);

            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown. A transport call may already have reached the broker, so persist the ambiguous outcome
            // and honor the cancellation so the hosted worker stops promptly.
            if (isFinalAttempt)
            {
                // No outcome is written for the last attempt. The row stays Publishing, exactly the state a
                // crash at this point leaves, and is recovered after RecoveryTimeout like any other in-doubt
                // attempt. A graceful stop must not be worse than a crash by demanding operator action.
                logger.LogInformation(
                    "Outbox message {MessageId} was interrupted by shutdown during its last permitted dispatch attempt; " +
                    "it will be recovered after the recovery timeout.", message.MessageId);
            }
            else
            {
                await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            // The publish attempt reached the transport; we cannot know whether the broker received
            // it before the failure, so this is ConfirmationUnknown, not a definite Failed.
            if (isFinalAttempt)
                return await AbandonAsync(message, attemptsStarted + 1, maxAttempts,
                    "Outbox dispatch attempts exhausted; the last publish attempt threw, so the delivery " +
                    "outcome is unknown.", ex, cancellationToken);

            logger.LogWarning(ex, "Outbox message {MessageId} publish attempt did not confirm success; marking ConfirmationUnknown.", message.MessageId);
            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
    }

    /// <summary>
    /// Whether the transport can positively confirm broker acceptance right now. An
    /// <see cref="IConfirmedEventBus"/> that reports otherwise through <see cref="IConditionalConfirmedEventBus"/>
    /// (Core NATS, RabbitMQ with publisher confirms disabled) is treated as unconfirming.
    /// </summary>
    private bool SupportsConfirmation() =>
        eventBus is IConfirmedEventBus &&
        (eventBus is not IConditionalConfirmedEventBus conditional || conditional.ConfirmationsAvailable);

    private async Task<OutboxMessageStatus> AbandonAsync(OutboxMessage message, int attempts, int maxAttempts,
        string reason, Exception? exception, CancellationToken cancellationToken)
    {
        // Warning, not Information: an abandoned message is unshipped business intent that needs a human.
        logger.LogWarning(exception,
            "Outbox message {MessageId} (saga {SagaId}) abandoned after {Attempts} dispatch attempts (limit {MaxAttempts}): {Reason} " +
            "It will not be dispatched again automatically and requires operator action.",
            message.MessageId, message.SagaId, attempts, maxAttempts, reason);

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

        var confirmed = SupportsConfirmation();
        var target = confirmed ? typeof(IConfirmedEventBus) : typeof(IEventBus);
        var instance = eventBus;
        var handlerType = string.IsNullOrWhiteSpace(envelope.HandlerType)
            ? null : ResolveType(envelope.HandlerType!, "handler");
        switch (envelope.Operation)
        {
            case OutboxOperationKind.Send:
                await InvokeGeneric(target, instance, confirmed ? nameof(IConfirmedEventBus.SendConfirmed) : nameof(IEventBus.Send),
                    [messageType], [message, handlerType, envelope.SagaId, cancellationToken]);
                return;
            case OutboxOperationKind.Publish:
                await InvokeGeneric(target, instance, confirmed ? nameof(IConfirmedEventBus.PublishConfirmed) : nameof(IEventBus.Publish),
                    [messageType], [message, handlerType, envelope.SagaId, cancellationToken]);
                return;
            case OutboxOperationKind.Respond:
                if (envelope.RequestBody == null || envelope.RequestHeaders == null ||
                    string.IsNullOrWhiteSpace(envelope.RequestType))
                    throw new InvalidOperationException($"Response envelope '{envelope.OutboxId}' has no durable request.");
                var requestType = ResolveType(envelope.RequestType!, "response request");
                var request = Deserialize(envelope.RequestBody, envelope.RequestHeaders, requestType);
                await InvokeGeneric(target, instance, confirmed ? nameof(IConfirmedEventBus.RespondConfirmed) : nameof(IEventBus.Respond),
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
