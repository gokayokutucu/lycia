// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// For Lazy<T>

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Messaging;
using Lycia.Saga.Extensions;

// ISagaDispatcher is likely here or in Lycia.Infrastructure.Abstractions

namespace Lycia.Eventing;

/// <summary>
/// Simple in-memory event bus that directly dispatches messages to registered handlers.
/// Suitable for development and testing only.
/// </summary>
public class InMemoryEventBus(Lazy<ISagaDispatcher> sagaDispatcherLazy, string? applicationId = null) : IEventBus
{
    public string ApplicationId { get; } = EndpointIdentityNormalizer.Default.Normalize(applicationId ?? "inmemory");

    public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        CommandEndpointResolver.Default.Resolve(command.GetType());
        RequestRouting.Prepare(command);
        // The sagaId parameter passed to Send/Publish is not directly used by DispatchAsync,
        // as DispatchAsync typically resolves SagaId from the message properties or generates it.
        // The original implementation also didn't use the sagaId parameter in its call to DispatchAsync.
        return sagaDispatcherLazy.Value.DispatchAsync(command, handlerType, sagaId, cancellationToken);
    }

    public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
        Guid? sagaId = null, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>
    {
        var endpoint = response.ResponseEndpoint
                       ?? (request as IRequestRoutingMetadata)?.ResponseEndpoint
                       ?? ApplicationId;
        response.PrepareResponse(request, sagaId ?? request.SagaId ?? Guid.Empty, endpoint);
        return sagaDispatcherLazy.Value.DispatchAsync<TRequest, TResponse>(response, handlerType, sagaId, cancellationToken);
    }

    public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (@event is IResponse)
            throw new InvalidOperationException(
                $"Response '{@event.GetType().FullName}' cannot be published. Use Respond(request, response)." );
        return sagaDispatcherLazy.Value.DispatchAsync(@event, handlerType, sagaId, cancellationToken);
    }

    public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
