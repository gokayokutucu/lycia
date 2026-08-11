// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using System.Reflection;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Abstractions.Serializers;

namespace Lycia.Scheduling;

/// <summary>Restores durable payloads and invokes the original event-bus semantic.</summary>
public sealed class EventBusSchedulingDispatcher(IOutgoingMessagePipeline outgoingPipeline, IMessageSerializer serializer)
    : ISchedulingDispatcher
{
    /// <summary>Creates a direct dispatcher for compatibility when no outgoing pipeline is supplied.</summary>
    public EventBusSchedulingDispatcher(IEventBus eventBus, IMessageSerializer serializer)
        : this(new DirectPipeline(eventBus), serializer)
    {
    }

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
                return InvokeGeneric(nameof(IOutgoingMessagePipeline.Send), new[] { messageType },
                    new object?[] { message, null, typedMessage.SagaId, cancellationToken });
            case ScheduledMessageKind.Event:
                return InvokeGeneric(nameof(IOutgoingMessagePipeline.Publish), new[] { messageType },
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
        return InvokeGeneric(nameof(IOutgoingMessagePipeline.Respond), new[] { requestType, responseType },
            new object?[] { request, response, null, sagaId, cancellationToken });
    }

    private object Deserialize(byte[] payload, IReadOnlyDictionary<string, object?> headers, Type type)
    {
        var (_, context) = serializer.CreateContextFor(type);
        return serializer.Deserialize(payload, headers, context);
    }

    private Task InvokeGeneric(string methodName, Type[] genericArguments, object?[] arguments)
    {
        var method = typeof(IOutgoingMessagePipeline).GetMethods()
            .Single(candidate => candidate.Name == methodName &&
                                 candidate.IsGenericMethodDefinition &&
                                 candidate.GetGenericArguments().Length == genericArguments.Length);
        try
        {
            return (Task)(method.MakeGenericMethod(genericArguments).Invoke(outgoingPipeline, arguments)
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

    private sealed class DirectPipeline(IEventBus eventBus) : IOutgoingMessagePipeline
    {
        public Task Send<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TCommand : ICommand =>
            eventBus.Send(command, handlerType, sagaId, cancellationToken);

        public Task Publish<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TEvent : IEvent =>
            eventBus.Publish(message, handlerType, sagaId, cancellationToken);

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> =>
            eventBus.Respond(request, response, handlerType, sagaId, cancellationToken);
    }
}
