// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// For Lazy<T>

#if NETSTANDARD2_0
using Lycia.Messaging;
using Lycia.Abstractions; // ISagaDispatcher is likely here or in Lycia.Infrastructure.Abstractions
using System.Threading;

namespace Lycia.Infrastructure.Eventing;

/// <summary>
/// Simple in-memory event bus that directly dispatches messages to registered handlers.
/// Suitable for development and testing only.
/// </summary>
public class InMemoryEventBus(Lazy<ISagaDispatcher> sagaDispatcherLazy) : IEventBus
{
    public void Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        Task sth;
        // The sagaId parameter passed to Send/Publish is not directly used by Dispatch,
        // as Dispatch typically resolves SagaId from the message properties or generates it.
        // The original implementation also didn't use the sagaId parameter in its call to Dispatch.
        sagaDispatcherLazy.Value
            .DispatchAsync(command, handlerType, sagaId, cancellationToken)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    public void Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        sagaDispatcherLazy.Value
            .DispatchAsync(@event, handlerType, sagaId, cancellationToken)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }

    public IEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> Consume(bool autoAck = true, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<IncomingMessage> ConsumeWithAck(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
} 
#endif