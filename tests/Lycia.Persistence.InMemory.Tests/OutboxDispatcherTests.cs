// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Outbox;
using Lycia.Extensions.Serialization;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.Persistence.InMemory.Tests;

public class OutboxDispatcherTests
{
    private sealed class DispatcherProbeEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class DispatcherProbeCommand : CommandBase;
    private sealed class DispatcherProbeResponse : ResponseBase<DispatcherProbeCommand>;

    private sealed class RecordingEventBus : IEventBus, IConfirmedEventBus
    {
        public readonly List<IEvent> Published = [];
        public readonly List<ICommand> Sent = [];
        public readonly List<IMessage> ResponseRequests = [];
        public readonly List<IResponse> Responded = [];
        public string ApplicationId => "TestApp";
        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TCommand : ICommand { Sent.Add(command); return Task.CompletedTask; }
        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TRequest : IMessage where TResponse : IResponse<TRequest> { ResponseRequests.Add(request); Responded.Add(response); return Task.CompletedTask; }
        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType, IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken = default) where TCommand : ICommand => Send(command, handlerType, sagaId, cancellationToken);
        public Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken = default) where TEvent : IEvent => Publish(message, handlerType, sagaId, cancellationToken);
        public Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken = default) where TRequest : IMessage where TResponse : IResponse<TRequest> => Respond(request, response, handlerType, sagaId, cancellationToken);
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
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = new OutboxDispatcher(store, bus, serializer, NullLogger<OutboxDispatcher>.Instance);

        var evt = new DispatcherProbeEvent { Payload = "hello" };
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);

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
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = new OutboxDispatcher(store, bus, serializer, NullLogger<OutboxDispatcher>.Instance);

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
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = new OutboxDispatcher(store, bus, serializer, NullLogger<OutboxDispatcher>.Instance);

        var evt = new DispatcherProbeEvent { Payload = "hello" };
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(evt, null, null);

        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(1, result.ConfirmationUnknown);
        Assert.Equal(0, result.Failed);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
    }

    [Fact]
    public async Task Pipeline_Restores_Send_And_Targeted_Respond_Semantics()
    {
        var store = new InMemoryOutboxStore();
        var bus = new RecordingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var pipeline = new OutboxOutgoingMessagePipeline(store, serializer);
        var dispatcher = new OutboxDispatcher(store, bus, serializer, NullLogger<OutboxDispatcher>.Instance);
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var parentMessageId = Guid.NewGuid();
        var sagaId = Guid.NewGuid();
        var request = new DispatcherProbeCommand
        {
            ApplicationId = "requesting-app",
            ResponseEndpoint = "requesting-app",
            RequestId = Guid.NewGuid(),
            CorrelationId = correlationId,
            CausationId = causationId,
            ParentMessageId = parentMessageId,
            SagaId = sagaId
        };
        var response = new DispatcherProbeResponse
        {
            ApplicationId = "responding-app",
            ResponseEndpoint = request.ResponseEndpoint,
            RequestId = request.RequestId,
            CorrelationId = correlationId,
            CausationId = request.MessageId,
            ParentMessageId = parentMessageId,
            SagaId = sagaId
        };

        await pipeline.Send(request, typeof(OutboxDispatcherTests), Guid.NewGuid());
        await pipeline.Respond(request, response, typeof(OutboxDispatcherTests), response.SagaId);
        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(2, result.Published);
        Assert.Single(bus.Sent);
        Assert.Single(bus.ResponseRequests);
        Assert.Single(bus.Responded);
        Assert.Empty(bus.Published);
        Assert.Equal(request.MessageId, bus.Sent[0].MessageId);
        Assert.Equal(response.MessageId, bus.Responded[0].MessageId);
        Assert.Equal(request.RequestId, ((IRequestRoutingMetadata)bus.ResponseRequests[0]).RequestId);
        Assert.Equal(request.ResponseEndpoint, ((IRequestRoutingMetadata)bus.ResponseRequests[0]).ResponseEndpoint);
        Assert.Equal(correlationId, bus.Responded[0].CorrelationId);
        Assert.Equal(request.MessageId, bus.Responded[0].CausationId);
        Assert.Equal(parentMessageId, bus.Responded[0].ParentMessageId);
        Assert.Equal(sagaId, bus.Responded[0].SagaId);
        Assert.Equal("responding-app", bus.Responded[0].ApplicationId);
    }

    [Fact]
    public async Task Permanent_Local_Failure_Does_Not_Stop_Later_Message_In_The_Same_Batch()
    {
        var store = new InMemoryOutboxStore();
        var bus = new RecordingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var dispatcher = new OutboxDispatcher(store, bus, serializer, NullLogger<OutboxDispatcher>.Instance);
        await store.AddAsync(new OutboxMessage(Guid.NewGuid(), "NoSuch.Type, NoSuchAssembly", "{}", "TestApp", null));
        var healthy = new DispatcherProbeEvent { Payload = "continue" };
        await new OutboxOutgoingMessagePipeline(store, serializer).Publish(healthy, null, null);

        var result = await dispatcher.DispatchPendingBatchAsync();

        Assert.Equal(2, result.Claimed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Published);
        Assert.Equal(healthy.MessageId, Assert.Single(bus.Published).MessageId);
    }

    [Fact]
    public async Task ConfirmationUnknown_Is_Redispatchable_But_Bounded_And_Keeps_MessageId()
    {
        var store = new InMemoryOutboxStore();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var pipeline = new OutboxOutgoingMessagePipeline(store, serializer);
        var dispatcher = new OutboxDispatcher(store, new ThrowingEventBus(), serializer,
            NullLogger<OutboxDispatcher>.Instance);
        var evt = new DispatcherProbeEvent { Payload = "stable" };
        await pipeline.Publish(evt, null, null);

        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(1, (await dispatcher.DispatchPendingBatchAsync(maxAttempts: 3)).ConfirmationUnknown);
        Assert.Equal(0, (await dispatcher.DispatchPendingBatchAsync(maxAttempts: 3)).Claimed);

        var durable = await store.GetByMessageIdAsync(evt.MessageId);
        Assert.Equal(evt.MessageId, durable!.MessageId);
        Assert.Equal(3, durable.RetryCount);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, durable.Status);
    }
}
