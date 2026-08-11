// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Extensions.Serialization;
using Lycia.Outbox;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Persistence.InMemory.Tests;

public class OutboxWorkerTests
{
    [Fact]
    public async Task RunOnceAsync_Claims_And_Dispatches_A_Captured_Message()
    {
        var store = new InMemoryOutboxStore();
        var bus = new ConfirmedRecordingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        await new OutboxOutgoingMessagePipeline(store, serializer)
            .Publish(new WorkerProbeEvent(), null, null);

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IEventBus>(bus);
        services.AddSingleton<Lycia.Saga.Abstractions.Serializers.IMessageSerializer>(serializer);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<OutboxDispatcher>>(
            NullLogger<OutboxDispatcher>.Instance);
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        await using var provider = services.BuildServiceProvider();
        var worker = new OutboxWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxWorkerOptions()), NullLogger<OutboxWorker>.Instance);

        var result = await worker.RunOnceAsync();

        Assert.Equal(1, result.Published);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Idle_Worker_Polls_Once_Then_Stops_Promptly_On_Cancellation()
    {
        var dispatcher = new SignalingDispatcher();
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxDispatcher>(dispatcher);
        await using var provider = services.BuildServiceProvider();
        var worker = new OutboxWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxWorkerOptions
            {
                PollInterval = TimeSpan.FromHours(1),
                MaxJitter = TimeSpan.Zero
            }), NullLogger<OutboxWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await dispatcher.FirstPass.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.PassCount);
    }

    private sealed class WorkerProbeEvent : EventBase;

    private sealed class SignalingDispatcher : IOutboxDispatcher
    {
        public TaskCompletionSource FirstPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PassCount { get; private set; }

        public Task<OutboxDispatchResult> DispatchPendingBatchAsync(int maxCount = 50,
            CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
        {
            PassCount++;
            FirstPass.TrySetResult();
            return Task.FromResult(new OutboxDispatchResult());
        }
    }

    private sealed class ConfirmedRecordingEventBus : IEventBus, IConfirmedEventBus
    {
        public List<IEvent> Published { get; } = [];
        public string ApplicationId => "TestApp";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public Task Publish<TEvent>(TEvent message, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TCommand : ICommand =>
            Send(command, handlerType, sagaId, cancellationToken);

        public Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TEvent : IEvent =>
            Publish(message, handlerType, sagaId, cancellationToken);

        public Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType,
            Guid? sagaId, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> =>
            Respond(request, response, handlerType, sagaId, cancellationToken);

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
