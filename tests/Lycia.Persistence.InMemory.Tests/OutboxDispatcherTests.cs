// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Outbox;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Lycia.Persistence.InMemory.Tests;

public class OutboxDispatcherTests
{
    private sealed class DispatcherProbeEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public readonly List<IEvent> Published = [];
        public string ApplicationId => "TestApp";
        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;
        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;
        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingEventBus : IEventBus
    {
        public string ApplicationId => "TestApp";
        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;
        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;
        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TEvent : IEvent =>
            throw new TimeoutException("broker did not respond");
        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_Publishes_Pending_Message_And_Marks_Published()
    {
        var store = new InMemoryOutboxStore();
        var bus = new RecordingEventBus();
        var dispatcher = new OutboxDispatcher(store, bus, NullLogger<OutboxDispatcher>.Instance);

        var evt = new DispatcherProbeEvent { Payload = "hello" };
        var message = new Saga.Abstractions.Outbox.OutboxMessage(
            evt.MessageId, typeof(DispatcherProbeEvent).AssemblyQualifiedName!, JsonConvert.SerializeObject(evt), "TestApp", null);
        await store.AddAsync(message);

        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.ConfirmationUnknown);
        Assert.Single(bus.Published);
        Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_Unresolvable_MessageType_Marks_Failed()
    {
        var store = new InMemoryOutboxStore();
        var bus = new RecordingEventBus();
        var dispatcher = new OutboxDispatcher(store, bus, NullLogger<OutboxDispatcher>.Instance);

        var messageId = Guid.NewGuid();
        await store.AddAsync(new Saga.Abstractions.Outbox.OutboxMessage(messageId, "NoSuch.Type, NoSuchAssembly", "{}", "TestApp", null));

        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(1, result.Failed);
        Assert.Empty(bus.Published);
        Assert.Equal(OutboxMessageStatus.Failed, (await store.GetByMessageIdAsync(messageId))!.Status);
    }

    [Fact]
    public async Task DispatchPendingBatchAsync_Publish_Exception_Marks_ConfirmationUnknown_Not_Failed()
    {
        var store = new InMemoryOutboxStore();
        var bus = new ThrowingEventBus();
        var dispatcher = new OutboxDispatcher(store, bus, NullLogger<OutboxDispatcher>.Instance);

        var evt = new DispatcherProbeEvent { Payload = "hello" };
        await store.AddAsync(new Saga.Abstractions.Outbox.OutboxMessage(
            evt.MessageId, typeof(DispatcherProbeEvent).AssemblyQualifiedName!, JsonConvert.SerializeObject(evt), "TestApp", null));

        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(1, result.ConfirmationUnknown);
        Assert.Equal(0, result.Failed);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
    }
}
