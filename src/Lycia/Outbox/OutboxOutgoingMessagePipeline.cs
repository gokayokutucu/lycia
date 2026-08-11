// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Serializers;
using Newtonsoft.Json;

namespace Lycia.Outbox;

/// <summary>Captures outgoing semantics in the configured durable Outbox instead of invoking a transport.</summary>
public sealed class OutboxOutgoingMessagePipeline(IOutboxStore store, IMessageSerializer serializer)
    : IOutgoingMessagePipeline
{
    /// <inheritdoc />
    public Task Send<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        CaptureAsync(OutboxOperationKind.Send, command, handlerType, sagaId, null, cancellationToken);

    /// <inheritdoc />
    public Task Publish<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default) where TEvent : IEvent =>
        CaptureAsync(OutboxOperationKind.Publish, message, handlerType, sagaId, null, cancellationToken);

    /// <inheritdoc />
    public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage where TResponse : IResponse<TRequest> =>
        CaptureAsync(OutboxOperationKind.Respond, response, handlerType, sagaId, request, cancellationToken);

    private Task CaptureAsync(OutboxOperationKind operation, IMessage message, Type? handlerType, Guid? sagaId,
        IMessage? request, CancellationToken cancellationToken)
    {
        var messageType = message.GetType();
        var (messageHeaders, messageContext) = serializer.CreateContextFor(messageType);
        var (body, serializedHeaders) = serializer.Serialize(message, messageContext);
        var envelope = new OutboxEnvelope
        {
            OutboxId = message.MessageId,
            MessageId = message.MessageId,
            Operation = operation,
            MessageType = RequiredTypeName(messageType),
            Body = body,
            Headers = CopyHeaders(serializedHeaders.Count == 0 ? messageHeaders : serializedHeaders),
            HandlerType = handlerType?.AssemblyQualifiedName,
            ApplicationId = message.ApplicationId,
            SagaId = sagaId ?? message.SagaId
        };

        if (request != null)
        {
            var requestType = request.GetType();
            var (_, requestContext) = serializer.CreateContextFor(requestType);
            var (requestBody, requestHeaders) = serializer.Serialize(request, requestContext);
            envelope.RequestType = RequiredTypeName(requestType);
            envelope.RequestBody = requestBody;
            envelope.RequestHeaders = CopyHeaders(requestHeaders);
        }

        var durable = new OutboxMessage(message.MessageId, envelope.MessageType,
            JsonConvert.SerializeObject(envelope), envelope.ApplicationId, envelope.SagaId);
        return store.AddAsync(durable, cancellationToken);
    }

    private static Dictionary<string, object?> CopyHeaders(IReadOnlyDictionary<string, object?> headers)
    {
        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers) copy[pair.Key] = pair.Value;
        return copy;
    }

    private static string RequiredTypeName(Type type) => type.AssemblyQualifiedName
        ?? throw new InvalidOperationException($"Message type '{type.FullName}' has no assembly-qualified name.");
}
