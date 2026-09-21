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
/// Regression coverage for Outbox retry exhaustion. A broker outage that outlasts the bounded attempt
/// budget must leave the message in a terminal, explained, operator-discoverable state — never in a
/// non-terminal status that no future claim query will ever return, which silently drops the message.
/// </summary>
public class OutboxRetryExhaustionTests
{
    private sealed class ProbeEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>A broker that is completely unavailable: every publish attempt throws.</summary>
    private sealed class BrokerDownEventBus : IEventBus
    {
        public int PublishAttempts;
        public string ApplicationId => "TestApp";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand =>
            throw new TimeoutException("broker unreachable");

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> =>
            throw new TimeoutException("broker unreachable");

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            PublishAttempts++;
            throw new TimeoutException("broker unreachable");
        }

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    /// <summary>An unconfirming transport that accepts every publish, like the current RabbitMQ publisher.</summary>
    private sealed class UnconfirmingEventBus : IEventBus
    {
        public int PublishAttempts;
        public string ApplicationId => "TestApp";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            PublishAttempts++;
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    /// <summary>A transport that positively confirms, like Kafka's idempotent acks=all producer or JetStream.</summary>
    private sealed class ConfirmingEventBus : IEventBus, IConfirmedEventBus
    {
        public string ApplicationId => "TestApp";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent => Task.CompletedTask;

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TEvent : IEvent => Task.CompletedTask;

        public Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType,
            Guid? sagaId, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;
    }

    private static OutboxDispatcher CreateDispatcher(IOutboxStore store, IEventBus bus) =>
        new(store, bus, new NewtonsoftJsonMessageSerializer(), new LyciaActivitySourceHolder(),
            NullLogger<OutboxDispatcher>.Instance);

    [Fact]
    public async Task Exhausted_attempts_against_an_unreachable_broker_end_in_a_terminal_explained_state()
    {
        const int maxAttempts = 3;
        var store = new InMemoryOutboxStore();
        var bus = new BrokerDownEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = CreateDispatcher(store, bus);

        var evt = new ProbeEvent { Payload = "business-critical" };
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);

        var abandoned = 0;
        for (var pass = 1; pass <= maxAttempts + 2; pass++)
        {
            var result = await dispatcher.DispatchPendingBatchAsync(50, default, maxAttempts, TimeSpan.Zero);
            abandoned += result.Abandoned;
        }

        Assert.Equal(maxAttempts, bus.PublishAttempts);
        Assert.Equal(1, abandoned);

        var row = await store.GetByMessageIdAsync(evt.MessageId);
        Assert.Equal(OutboxMessageStatus.Abandoned, row!.Status);
        Assert.Equal(maxAttempts, row.RetryCount);

        // The reason must be recorded, so an operator finding this row can tell what happened.
        Assert.NotNull(row.FailureInfo);
        Assert.Contains("exhausted", row.FailureInfo!.Reason, StringComparison.OrdinalIgnoreCase);

        // Terminal: never handed back again, and no further publish attempts are made.
        var after = await dispatcher.DispatchPendingBatchAsync(50, default, maxAttempts, TimeSpan.Zero);
        Assert.Equal(0, after.Claimed);
        Assert.Equal(maxAttempts, bus.PublishAttempts);
    }

    [Fact]
    public async Task An_unconfirmed_but_accepted_publish_is_only_retried_after_the_recovery_window()
    {
        var store = new InMemoryOutboxStore();
        var bus = new UnconfirmingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = CreateDispatcher(store, bus);

        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(new ProbeEvent(), null, null);

        // First pass publishes once and records ConfirmationUnknown.
        var first = await dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.FromMinutes(5));
        Assert.Equal(1, first.ConfirmationUnknown);
        Assert.Equal(1, bus.PublishAttempts);

        // Immediately afterwards the message must NOT be re-claimed: otherwise the whole attempt budget
        // burns in a burst and the same message is republished that many times back to back.
        for (var pass = 0; pass < 3; pass++)
        {
            var again = await dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.FromMinutes(5));
            Assert.Equal(0, again.Claimed);
        }

        Assert.Equal(1, bus.PublishAttempts);
    }

    /// <summary>
    /// An unconfirming transport such as RabbitMQ reports every successful publish as ConfirmationUnknown.
    /// Running out of attempts that the broker accepted is ordinary at-least-once delivery, so it must not
    /// be escalated to Abandoned — otherwise essentially every delivered message would demand operator action.
    /// </summary>
    [Fact]
    public async Task Exhausting_attempts_the_transport_accepted_stays_ConfirmationUnknown_not_Abandoned()
    {
        const int maxAttempts = 3;
        var store = new InMemoryOutboxStore();
        var bus = new UnconfirmingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = CreateDispatcher(store, bus);

        var evt = new ProbeEvent();
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);

        var abandoned = 0;
        for (var pass = 1; pass <= maxAttempts + 2; pass++)
            abandoned += (await dispatcher.DispatchPendingBatchAsync(50, default, maxAttempts, TimeSpan.Zero)).Abandoned;

        Assert.Equal(maxAttempts, bus.PublishAttempts);
        Assert.Equal(0, abandoned);

        var row = await store.GetByMessageIdAsync(evt.MessageId);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, row!.Status);
        Assert.Equal(maxAttempts, row.RetryCount);

        // Still bounded: the cap stops further redispatch.
        Assert.Equal(0, (await dispatcher.DispatchPendingBatchAsync(50, default, maxAttempts, TimeSpan.Zero)).Claimed);
        Assert.Equal(maxAttempts, bus.PublishAttempts);
    }

    [Fact]
    public async Task A_confirmed_transport_never_abandons_on_the_final_attempt()
    {
        var store = new InMemoryOutboxStore();
        var bus = new ConfirmingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = CreateDispatcher(store, bus);

        var evt = new ProbeEvent();
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);

        // maxAttempts: 1 makes the very first attempt the final permitted one.
        var result = await dispatcher.DispatchPendingBatchAsync(50, default, 1, TimeSpan.Zero);

        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Abandoned);
        Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
    }
}
