// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Serialization;
using Lycia.Observability;
using Lycia.Outbox;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.Persistence.InMemory.Tests;

/// <summary>
/// A transport can implement <see cref="IConfirmedEventBus"/> yet be unable to confirm in its current
/// configuration (Core NATS has no publish acknowledgement; RabbitMQ can have publisher confirms turned off).
/// The dispatcher must then treat it as unconfirming instead of calling methods that cannot confirm.
/// </summary>
public class OutboxConditionalConfirmationTests
{
    private sealed class ProbeEvent : EventBase;

    private sealed class ConditionalBus(bool confirmationsAvailable) : IEventBus, IConditionalConfirmedEventBus
    {
        public int PlainPublishes;
        public int ConfirmedPublishes;
        public string ApplicationId => "Test";
        public bool ConfirmationsAvailable => confirmationsAvailable;

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            PlainPublishes++;
            return Task.CompletedTask;
        }

        public Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        // Like NatsEventBus in Core mode, these cannot confirm and throw when called.
        public Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            ConfirmedPublishes++;
            if (!confirmationsAvailable) throw new InvalidOperationException("this transport cannot confirm a publish");
            return Task.CompletedTask;
        }

        public Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType,
            Guid? sagaId, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static async Task<(OutboxDispatchResult Result, InMemoryOutboxStore Store, Guid Id)> Dispatch(ConditionalBus bus)
    {
        var store = new InMemoryOutboxStore();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var evt = new ProbeEvent();
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);
        var dispatcher = new OutboxDispatcher(store, bus, serializer, new LyciaActivitySourceHolder(),
            NullLogger<OutboxDispatcher>.Instance);
        return (await dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.Zero), store, evt.MessageId);
    }

    [Fact]
    public async Task A_transport_that_cannot_confirm_is_treated_as_unconfirming()
    {
        var bus = new ConditionalBus(confirmationsAvailable: false);

        var (result, store, id) = await Dispatch(bus);

        Assert.Equal(0, bus.ConfirmedPublishes);   // the confirmed method that would throw was never called
        Assert.Equal(1, bus.PlainPublishes);
        Assert.Equal(1, result.ConfirmationUnknown);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(id))!.Status);
    }

    [Fact]
    public async Task A_transport_that_can_confirm_uses_the_confirmed_path_and_becomes_Published()
    {
        var bus = new ConditionalBus(confirmationsAvailable: true);

        var (result, store, id) = await Dispatch(bus);

        Assert.Equal(1, bus.ConfirmedPublishes);
        Assert.Equal(0, bus.PlainPublishes);
        Assert.Equal(1, result.Published);
        Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(id))!.Status);
    }
}
