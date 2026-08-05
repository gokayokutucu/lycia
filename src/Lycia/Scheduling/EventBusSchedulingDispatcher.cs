// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using System.Reflection;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Serializers;

namespace Lycia.Scheduling;

/// <summary>Restores durable payloads and invokes the original event-bus semantic.</summary>
public sealed class EventBusSchedulingDispatcher(IEventBus eventBus, IMessageSerializer serializer)
    : ISchedulingDispatcher
{
    /// <inheritdoc />
    public Task DispatchAsync(ScheduleRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        var messageType = ResolveType(record.MessageType, "scheduled message");
        var message = Deserialize(record.Payload, record.Headers, messageType);
        if (!(message is IMessage typedMessage))
            throw new InvalidOperationException($"Scheduled payload '{record.MessageType}' is not a Lycia message.");
        if (typedMessage.MessageId != record.MessageId)
            throw new InvalidOperationException(
                $"Scheduled payload MessageId '{typedMessage.MessageId}' differs from stored MessageId '{record.MessageId}'.");

        switch (record.MessageKind)
        {
            case ScheduledMessageKind.Command:
                return InvokeGeneric(nameof(IEventBus.Send), new[] { messageType },
                    new object?[] { message, null, typedMessage.SagaId, cancellationToken });
            case ScheduledMessageKind.Event:
                return InvokeGeneric(nameof(IEventBus.Publish), new[] { messageType },
                    new object?[] { message, null, typedMessage.SagaId, cancellationToken });
            case ScheduledMessageKind.Response:
                return DispatchResponseAsync(record, messageType, message, typedMessage.SagaId, cancellationToken);
            default:
                throw new InvalidOperationException($"Unsupported scheduled message kind '{record.MessageKind}'.");
        }
    }

    private Task DispatchResponseAsync(ScheduleRecord record, Type responseType, object response, Guid? sagaId,
        CancellationToken cancellationToken)
    {
        if (record.RequestPayload == null || record.RequestHeaders == null || string.IsNullOrWhiteSpace(record.RequestType))
            throw new InvalidOperationException(
                $"Scheduled response '{record.ScheduleId}' has no durable request payload for targeted dispatch.");
        var requestType = ResolveType(record.RequestType
                                      ?? throw new InvalidOperationException("Scheduled response request type is missing."),
            "scheduled response request");
        var request = Deserialize(record.RequestPayload, record.RequestHeaders, requestType);
        return InvokeGeneric(nameof(IEventBus.Respond), new[] { requestType, responseType },
            new object?[] { request, response, null, sagaId, cancellationToken });
    }

    private object Deserialize(byte[] payload, IReadOnlyDictionary<string, object?> headers, Type type)
    {
        var (_, context) = serializer.CreateContextFor(type);
        return serializer.Deserialize(payload, headers, context);
    }

    private Task InvokeGeneric(string methodName, Type[] genericArguments, object?[] arguments)
    {
        var method = typeof(IEventBus).GetMethods()
            .Single(candidate => candidate.Name == methodName &&
                                 candidate.IsGenericMethodDefinition &&
                                 candidate.GetGenericArguments().Length == genericArguments.Length);
        try
        {
            return (Task)(method.MakeGenericMethod(genericArguments).Invoke(eventBus, arguments)
                          ?? throw new InvalidOperationException($"Event bus method '{methodName}' returned null."));
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
        }
    }

    private static Type ResolveType(string assemblyQualifiedName, string role) =>
        Type.GetType(assemblyQualifiedName, throwOnError: false)
        ?? throw new InvalidOperationException($"Unable to resolve {role} type '{assemblyQualifiedName}'.");
}
