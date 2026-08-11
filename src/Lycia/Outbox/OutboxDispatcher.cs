// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;
using Lycia.Common.SagaSteps;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lycia.Outbox;

/// <inheritdoc cref="IOutboxDispatcher" />
public class OutboxDispatcher(IOutboxStore outboxStore, IEventBus eventBus, IMessageSerializer serializer,
    ILogger<OutboxDispatcher> logger)
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

            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, cancellationToken);
            return OutboxMessageStatus.ConfirmationUnknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A transport call may already have reached the broker. Persist the ambiguous outcome,
            // then honor shutdown cancellation so the hosted worker stops promptly.
            await outboxStore.MarkConfirmationUnknownAsync(message.MessageId, CancellationToken.None);
            throw;
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

    private async Task DispatchSemanticAsync(OutboxEnvelope envelope, Type messageType, object message,
        CancellationToken cancellationToken)
    {
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
