// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using FluentAssertions;
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Compensating;
using Lycia.Dispatching;
using StackExchange.Redis;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Serialization;
using Lycia.Extensions.Stores;



using Lycia.Helpers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.IntegrationTests;

/// <summary>
/// Integration test for Saga compensation chain using RabbitMQ and Redis.
/// </summary>
public class RabbitMqSagaCompensationIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private string RabbitMqConnectionString =>
           //"amqp://guest:guest@127.0.0.1:5672/"; 
        _rabbitMqContainer.GetConnectionString();

    private string RedisEndpoint =>
           //"127.0.0.1:6379"; 
        $"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}";

    public async Task InitializeAsync()
    {
        await _rabbitMqContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _rabbitMqContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task ResponseProducedByOneReplica_IsContinuedByAnotherReplica_FromSharedRedisState()
    {
        const string producerApplicationId = "Replica-App";
        const string consumerApplicationId = "replica_app";
        var producerQueue = MessagingNamingHelper.GetResponseQueueName(
            typeof(ReplicaResponse), producerApplicationId);
        var consumerQueue = MessagingNamingHelper.GetResponseQueueName(
            typeof(ReplicaResponse), consumerApplicationId);
        producerQueue.Should().Be(consumerQueue);

        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            [producerQueue] = (typeof(ReplicaResponse), typeof(FailingSagaHandler))
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        await using var producerBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            new EventBusOptions
            {
                ApplicationId = producerApplicationId,
                MessageTTL = TimeSpan.FromMinutes(1),
                ConnectionString = RabbitMqConnectionString
            },
            serializer);
        await using var consumerBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            new EventBusOptions
            {
                ApplicationId = consumerApplicationId,
                MessageTTL = TimeSpan.FromMinutes(1),
                ConnectionString = RabbitMqConnectionString
            },
            serializer);

        await using var redisA = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        await using var redisB = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var storeA = new RedisSagaStore(
            redisA.GetDatabase(), producerBus, null!, null!,
            new SagaStoreOptions { ApplicationId = producerApplicationId, StepLogTtl = TimeSpan.FromMinutes(5) });
        var storeB = new RedisSagaStore(
            redisB.GetDatabase(), consumerBus, null!, null!,
            new SagaStoreOptions { ApplicationId = consumerApplicationId, StepLogTtl = TimeSpan.FromMinutes(5) });

        var sagaId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        await storeA.SaveSagaDataAsync(sagaId, new DummySagaData { SomeField = "persisted-by-replica-a" });
        var request = new TestSagaCommand
        {
            SagaId = sagaId,
            CorrelationId = workflowId,
            RequestId = Guid.Empty,
            ResponseEndpoint = producerApplicationId,
            Message = "produced-by-replica-a"
        };
        request.RequestId = request.MessageId;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var continuation = Task.Run(async () =>
        {
            await foreach (var incoming in consumerBus.ConsumeWithAckAsync(timeout.Token))
            {
                await incoming.Ack();
                var normalized = serializer.NormalizeTransportHeaders(incoming.Headers);
                var (_, context) = serializer.CreateContextFor(typeof(ReplicaResponse));
                var received = (ReplicaResponse)serializer.Deserialize(incoming.Body, normalized, context);
                var loaded = await storeB.LoadSagaDataAsync<DummySagaData>(received.SagaId!.Value);
                return (Response: received, State: loaded);
            }

            throw new InvalidOperationException("Replica B response consumer completed early.");
        }, timeout.Token);

        await Task.Delay(300, timeout.Token);
        await producerBus.Respond(
            request,
            new ReplicaResponse { Message = "continued-by-replica-b" },
            cancellationToken: timeout.Token);

        var result = await continuation.WaitAsync(timeout.Token);
        result.State.SomeField.Should().Be("persisted-by-replica-a");
        result.Response.SagaId.Should().Be(sagaId);
        result.Response.CorrelationId.Should().Be(workflowId);
        result.Response.RequestId.Should().Be(request.MessageId);
        result.Response.CausationId.Should().Be(request.MessageId);
        result.Response.ParentMessageId.Should().Be(request.MessageId);
        result.Response.ResponseEndpoint.Should().Be("replicaapp");
        result.Response.MessageId.Should().NotBe(request.MessageId);
    }

    [Fact]
    public async Task CompensationChain_Should_Be_Idempotent_For_Multiple_Compensation_Attempts()
    {
        // Arrange: Setup saga chain handlers (grandparent -> parent -> child)
        var applicationId = $"{nameof(RabbitMqSagaCompensationIntegrationTests)}-{nameof(SagaChain_Should_Compensate_On_Failure)}-{Guid.NewGuid():N}";
        var handlerTypeGrandparent = typeof(GrandparentCompensationSagaHandler);
        var handlerTypeParent = typeof(ParentCompensationSagaHandler);
        var handlerTypeChild = typeof(ChildCompensationSagaHandler);

        // Unique IDs for saga and each step
        var sagaId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Prepare dummy messages
        var grandparentMsg = new DummyGrandparentEvent
            { SagaId = sagaId, MessageId = grandparentId, Message = "grandparent" };
        var parentMsg = new DummyParentEvent
            { SagaId = sagaId, MessageId = parentId, ParentMessageId = grandparentId, Message = "parent" };
        var childMsg = new RoutedDummyChildEvent
        {
            SagaId = sagaId,
            MessageId = childId,
            ParentMessageId = parentId,
            RequestId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            Message = "trigger-failure"
        };
        childMsg.RequestId.Should().NotBe(parentId);
        childMsg.CausationId.Should().NotBe(parentId);

        // EventBus and Redis-backed SagaStore setup
        var grandParentQueueName = Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { grandParentQueueName, (typeof(DummyGrandparentEvent), typeof(GrandparentCompensationSagaHandler)) },
            { parentQueueName, (typeof(DummyParentEvent), typeof(ParentCompensationSagaHandler)) },
            { childQueueName, (typeof(DummyChildEvent), typeof(ChildCompensationSagaHandler)) },
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId, 
            MessageTTL = TimeSpan.FromSeconds(10),
            ConnectionString = this.RabbitMqConnectionString
        };
        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();
        var sagaStoreOptions = new SagaStoreOptions
            { ApplicationId = applicationId, StepLogTtl = TimeSpan.FromMinutes(5) };
        var dummySagaIdGenerator = new TestSagaIdGenerator(sagaId);

        // Register compensation handlers (clear static invocation logs)
        GrandparentCompensationSagaHandler.Invocations.Clear();
        ParentCompensationSagaHandler.Invocations.Clear();
        ChildCompensationSagaHandler.Invocations.Clear();

        var services = new ServiceCollection();
        services.AddSingleton<GrandparentCompensationSagaHandler>();
        services.AddSingleton<ParentCompensationSagaHandler>();
        services.AddSingleton<ChildCompensationSagaHandler>();
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator, serializer));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));
        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(
                sp.GetRequiredService<ISagaStore>(),
                dummySagaIdGenerator,
                sp,
                NullLogger<SagaDispatcher>.Instance));

        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var coordinator = serviceProvider.GetRequiredService<ISagaCompensationCoordinator>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as Compensated
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg, (SagaStepFailureInfo?)null);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg, (SagaStepFailureInfo?)null);
        await sagaStore.LogStepAsync(sagaId, childId, parentId, typeof(DummyChildEvent), StepStatus.Failed,
            handlerTypeChild, childMsg, (SagaStepFailureInfo?)null);

        // Act 1: Compensate child, parent and grandparent (normal compensation flow)
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyChildEvent), typeof(ChildCompensationSagaHandler), childMsg); 
        
        await WaitForInvocationsAsync(expectedParentCount: 1, expectedGrandparentCount: 1);

        // Save initial invocation counts for later assertions
        var parentInitialCount = ParentCompensationSagaHandler.Invocations.Count;
        var grandparentInitialCount = GrandparentCompensationSagaHandler.Invocations.Count;

        // Act 2: Try to compensate parent and grandparent again (should be idempotent)
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyChildEvent), typeof(ChildCompensationSagaHandler), childMsg); 
        
        await WaitForInvocationsAsync(expectedParentCount: 1, expectedGrandparentCount: 1);

        // Assert: No new invocations should be added
        ParentCompensationSagaHandler.Invocations.Count.Should().Be(parentInitialCount);
        GrandparentCompensationSagaHandler.Invocations.Count.Should().Be(grandparentInitialCount);

        // Assert: Step status in Redis should still be single compensated per step
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);
        steps.Values.Count(x => x.Status == StepStatus.Compensated).Should().Be(3); // parent and grandparent and child should be compensated only once

        await eventBus.DisposeAsync();
    }
    
    private static async Task WaitForInvocationsAsync(
        int expectedParentCount,
        int expectedGrandparentCount,
        int timeoutMs = 60000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (ParentCompensationSagaHandler.Invocations.Count < expectedParentCount ||
               GrandparentCompensationSagaHandler.Invocations.Count < expectedGrandparentCount)
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Handler invocations not completed in expected time!");
            await Task.Delay(25); // Short poll
        }
    }

    [Fact]
    public async Task CompensationChain_Should_Halt_If_Child_Is_CompensationFailed()
    {
        // Arrange: Setup saga chain handlers (grandparent -> parent -> child)
        var applicationId = $"{nameof(RabbitMqSagaCompensationIntegrationTests)}-{nameof(SagaChain_Should_Compensate_On_Failure)}-{Guid.NewGuid():N}";
        var handlerTypeGrandparent = typeof(GrandparentCompensationSagaHandler);
        var handlerTypeParent = typeof(ParentCompensationSagaHandler);
        var handlerTypeChild = typeof(ChildCompensationSagaHandler);

        // Unique IDs for saga and each step
        var sagaId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Prepare dummy messages
        var grandparentMsg = new DummyGrandparentEvent
            { SagaId = sagaId, MessageId = grandparentId, Message = "grandparent" };
        var parentMsg = new DummyParentEvent
            { SagaId = sagaId, MessageId = parentId, ParentMessageId = grandparentId, Message = "parent" };
        var childMsg = new DummyChildEvent
        {
            IsCompensationFailed = true,
            IsFailed = true,
            SagaId = sagaId, MessageId = childId, ParentMessageId = parentId, Message = "trigger-failure"
        };

        // EventBus and Redis-backed SagaStore setup
        var grandParentQueueName = Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { grandParentQueueName, (typeof(DummyGrandparentEvent), typeof(GrandparentCompensationSagaHandler)) },
            { parentQueueName, (typeof(DummyParentEvent), typeof(ParentCompensationSagaHandler)) },
            { childQueueName, (typeof(DummyChildEvent), typeof(ChildCompensationSagaHandler)) },
        };
        var serializer = new NewtonsoftJsonMessageSerializer();
        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId, 
            MessageTTL = TimeSpan.FromSeconds(10),
            ConnectionString = RabbitMqConnectionString
        };
        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();
        var sagaStoreOptions = new SagaStoreOptions
            { ApplicationId = applicationId, StepLogTtl = TimeSpan.FromMinutes(5) };
        var dummySagaIdGenerator = new TestSagaIdGenerator(sagaId);

        // Register compensation handlers (clear static invocation logs)
        GrandparentCompensationSagaHandler.Invocations.Clear();
        ParentCompensationSagaHandler.Invocations.Clear();
        ChildCompensationSagaHandler.Invocations.Clear();

        var services = new ServiceCollection();
        services.AddSingleton<GrandparentCompensationSagaHandler>();
        services.AddSingleton<ParentCompensationSagaHandler>();
        services.AddSingleton<ChildCompensationSagaHandler>();
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator, serializer));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));
        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(
                sp.GetRequiredService<ISagaStore>(),
                dummySagaIdGenerator,
                sp,
                NullLogger<SagaDispatcher>.Instance));


        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var sagaDispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as CompensationFailed
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg, (SagaStepFailureInfo?)null);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg, (SagaStepFailureInfo?)null);

        // Act 1: Call the protected DispatchCompensationHandlersAsync method using reflection (simulate compensation chain)
        await sagaDispatcher.DispatchAsync(childMsg, typeof(ChildCompensationSagaHandler), sagaId: sagaId, CancellationToken.None);

        await WaitForConditionAsync(() =>
                GrandparentCompensationSagaHandler.Invocations.Count > 0 ||
                ParentCompensationSagaHandler.Invocations.Count > 0 ||
                ChildCompensationSagaHandler.Invocations.Count > 0
            , timeoutMs: 20000);

        // Assert: Chain should not proceed if child is CompensationFailed
        GrandparentCompensationSagaHandler.Invocations.Should()
            .BeEmpty("Grandparent compensation should not be invoked if child compensation failed");
        ParentCompensationSagaHandler.Invocations.Should()
            .BeEmpty("Parent compensation should not be invoked if child compensation failed");
        ChildCompensationSagaHandler.Invocations.Should().ContainSingle().And.Contain("ChildCompensationSagaHandler");

        // Also validate that Redis steps are stayed as Completed
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);
        steps.Values.Count(x => x.Status == StepStatus.Completed).Should()
            .BeGreaterThanOrEqualTo(2); // Both parent and grandparent should be stayed as Completed.
        steps.Values.Count(x => x.Status == StepStatus.CompensationFailed).Should()
            .Be(1); // Last child step should be marked as CompensationFailed.

        await eventBus.DisposeAsync();
    }

    [Fact]
    public async Task CompensationChain_Should_Recursively_Compensate_Parent_And_Grandparent_When_Child_Is_Compensated()
    {
        // Arrange: Setup saga chain handlers (grandparent -> parent -> child)
        var applicationId = $"{nameof(RabbitMqSagaCompensationIntegrationTests)}-{nameof(SagaChain_Should_Compensate_On_Failure)}-{Guid.NewGuid():N}";
        var handlerTypeGrandparent = typeof(GrandparentCompensationSagaHandler);
        var handlerTypeParent = typeof(ParentCompensationSagaHandler);
        var handlerTypeChild = typeof(ChildCompensationSagaHandler);
        
        var serializer = new NewtonsoftJsonMessageSerializer();

        // Unique IDs for saga and each step
        var sagaId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Prepare dummy messages
        var grandparentMsg = new DummyGrandparentEvent
            { SagaId = sagaId, MessageId = grandparentId, Message = "grandparent" };
        var parentMsg = new DummyParentEvent
            { SagaId = sagaId, MessageId = parentId, ParentMessageId = grandparentId, Message = "parent" };
        var childMsg = new DummyChildEvent
        {
            IsCompensationFailed = false,
            IsFailed = true,
            SagaId = sagaId, MessageId = childId, ParentMessageId = parentId, Message = "trigger-failure"
        };

        // EventBus and Redis-backed SagaStore setup
        var grandParentQueueName = Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, (Type, Type)>
        {
            { grandParentQueueName, (typeof(DummyGrandparentEvent), typeof(GrandparentCompensationSagaHandler)) },
            { parentQueueName, (typeof(DummyParentEvent), typeof(ParentCompensationSagaHandler)) },
            { childQueueName, (typeof(DummyChildEvent), typeof(ChildCompensationSagaHandler)) },
        };
        var eventBusOptions = new EventBusOptions
        {
            
            ApplicationId = applicationId, 
            MessageTTL = TimeSpan.FromSeconds(10),
            ConnectionString = RabbitMqConnectionString
        };
        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions, serializer);

        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();
        var sagaStoreOptions = new SagaStoreOptions
            { ApplicationId = applicationId, StepLogTtl = TimeSpan.FromMinutes(5) };
        var dummySagaIdGenerator = new TestSagaIdGenerator(sagaId);

        // Register compensation handlers (clear static invocation logs)
        GrandparentCompensationSagaHandler.Invocations.Clear();
        ParentCompensationSagaHandler.Invocations.Clear();
        ChildCompensationSagaHandler.Invocations.Clear();

        var services = new ServiceCollection();
        services.AddSingleton<GrandparentCompensationSagaHandler>();
        services.AddSingleton<ParentCompensationSagaHandler>();
        services.AddSingleton<ChildCompensationSagaHandler>();

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator, serializer));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));

        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(
                sp.GetRequiredService<ISagaStore>(),
                dummySagaIdGenerator,
                sp,
                NullLogger<SagaDispatcher>.Instance));

        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var sagaDispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as CompensationFailed
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg, (SagaStepFailureInfo?)null);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg, (SagaStepFailureInfo?)null);

        // Act 1: Call the protected DispatchCompensationHandlersAsync method using reflection (simulate compensation chain)
        await sagaDispatcher.DispatchAsync(childMsg, typeof(ChildCompensationSagaHandler), sagaId: sagaId, CancellationToken.None);

        await WaitForConditionAsync(() =>
                GrandparentCompensationSagaHandler.Invocations.Count == 1 &&
                ParentCompensationSagaHandler.Invocations.Count == 1 &&
                ChildCompensationSagaHandler.Invocations.Count == 1
            , timeoutMs: 3000);

        // Assert: Chain should not proceed if child is CompensationFailed
        GrandparentCompensationSagaHandler.Invocations.Should().ContainSingle().And
            .Contain("GrandparentCompensationSagaHandler");
        ParentCompensationSagaHandler.Invocations.Should().ContainSingle().And.Contain("ParentCompensationSagaHandler");
        ChildCompensationSagaHandler.Invocations.Should().ContainSingle().And.Contain("ChildCompensationSagaHandler");

        // Also validate that Redis steps are stayed as Completed
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);
        steps.Values.Count(x => x.Status == StepStatus.Compensated).Should()
            .Be(3); // Both parent and grandparent should be stayed as Completed.

        await eventBus.DisposeAsync();
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = 3000, int pollMs = 50)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition not met in expected time.");
            await Task.Delay(pollMs);
        }
    }


    [Fact]
    public async Task
        CompensationChain_Should_Recursively_Compensate_Parent_And_Grandparent_When_Steps_Are_Compensated()
    {
        // Arrange: Setup the saga chain with grandparent -> parent -> child handlers.
        var applicationId = $"{nameof(RabbitMqSagaCompensationIntegrationTests)}-{nameof(SagaChain_Should_Compensate_On_Failure)}-{Guid.NewGuid():N}";
        var handlerTypeGrandparent = typeof(GrandparentCompensationHandler);
        var handlerTypeParent = typeof(ParentCompensationHandler);
        var handlerTypeChild = typeof(ChildCompensationHandler);

        // Unique IDs for saga and steps.
        var sagaId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Prepare dummy event messages for each handler.
        var grandparentMsg = new DummyEvent { SagaId = sagaId, MessageId = grandparentId, Message = "grandparent" };
        var parentMsg = new DummyEvent
            { SagaId = sagaId, MessageId = parentId, ParentMessageId = grandparentId, Message = "parent" };
        var childMsg = new DummyEvent
            { SagaId = sagaId, MessageId = childId, ParentMessageId = parentId, Message = "trigger-failure" };

        // Configure EventBus (RabbitMQ) and SagaStore (Redis).
        var queueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(DummyEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, (Type, Type)> { { queueName, (typeof(DummyEvent), typeof(ChildCompensationHandler)) } };
        var serializer = new NewtonsoftJsonMessageSerializer();
        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId, 
            MessageTTL = TimeSpan.FromSeconds(10),
            ConnectionString = RabbitMqConnectionString
        };
        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions, serializer);

        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();
        var sagaStoreOptions = new SagaStoreOptions
            { ApplicationId = applicationId, StepLogTtl = TimeSpan.FromMinutes(5) };
        var dummySagaIdGenerator = new TestSagaIdGenerator(sagaId);

        // Register compensation handlers.
        GrandparentCompensationHandler.Invocations.Clear();
        ParentCompensationHandler.Invocations.Clear();
        ChildCompensationHandler.Invocations.Clear();

        var services = new ServiceCollection();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, GrandparentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ParentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ChildCompensationHandler>();
        services.AddSingleton<IMessageSerializer>(serializer);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator, serializer));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));

        var provider = services.BuildServiceProvider();

        var sagaStore = provider.GetRequiredService<ISagaStore>();
        var coordinator = provider.GetRequiredService<ISagaCompensationCoordinator>();

        // Pre-populate the saga steps as "Completed".
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg, (SagaStepFailureInfo?)null);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg, (SagaStepFailureInfo?)null);

        // Mark child as Compensated (simulate successful compensation at leaf)
        await sagaStore.LogStepAsync(sagaId, childId, parentId, typeof(DummyEvent), StepStatus.Failed,
            handlerTypeChild, childMsg, (SagaStepFailureInfo?)null);

        // First compensation attempt should compensate parent only
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), typeof(ChildCompensationHandler), childMsg);

        ParentCompensationHandler.Invocations.Should().ContainSingle().And.Contain("ParentCompensationHandler");
        GrandparentCompensationHandler.Invocations.Should().BeEmpty();

        // Now mark parent as Compensated, which should trigger grandparent
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent),  typeof(ParentCompensationHandler), parentMsg);

        GrandparentCompensationHandler.Invocations.Should().ContainSingle().And
            .Contain("GrandparentCompensationHandler");

        // Verify that in Redis both parent and grandparent steps are marked as Compensated
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);
        steps.Values.Count(x => x.Status == StepStatus.Compensated).Should()
            .BeGreaterThanOrEqualTo(2); // Both parent and grandparent should be compensated.

        await eventBus.DisposeAsync();
    }

    [Fact]
    public async Task SagaChain_Should_Compensate_On_Failure()
    {
        // Arrange: Set up EventBus and Redis-backed SagaStore
        var applicationId = $"{nameof(RabbitMqSagaCompensationIntegrationTests)}-{nameof(SagaChain_Should_Compensate_On_Failure)}-{Guid.NewGuid():N}";
        var handlerType = typeof(FailingSagaHandler);

        var queueName =
            Lycia.Helpers.MessagingNamingHelper.GetQueueName(typeof(TestSagaCommand), handlerType, applicationId);
        var queueTypeMap = new Dictionary<string, (Type, Type)> { { queueName, (typeof(TestSagaCommand), typeof(FailingSagaHandler)) } };
        var eventBusOptions = new EventBusOptions
        {
            ApplicationId = applicationId, 
            MessageTTL = TimeSpan.FromSeconds(30),
            ConnectionString = RabbitMqConnectionString
        };

        var serializer = new NewtonsoftJsonMessageSerializer();
        var eventBus = await RabbitMqEventBus.CreateAsync(
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions,
            serializer);

        // Set up Redis connection and store
        var redis = await ConnectionMultiplexer.ConnectAsync(RedisEndpoint);
        var redisDb = redis.GetDatabase();

        var sagaStoreOptions = new SagaStoreOptions
        {
            ApplicationId = applicationId,
            StepLogTtl = TimeSpan.FromMinutes(5)
        };

        var dummySagaIdGenerator = new TestSagaIdGenerator(Guid.Parse("C6B819C0-98E6-4A3C-AD28-385F7ACF3E1D"));
        var dummyCompensationCoordinator = new DummySagaCompensationCoordinator();
        var sagaStore = new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator, dummyCompensationCoordinator,
            sagaStoreOptions);

        var testCommand = new TestSagaCommand
        {
            SagaId = Guid.NewGuid(),
            Message = "trigger-failure"
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var receivedMessages = new List<TestSagaCommand>();

        var starterMessageId = Guid.NewGuid();
        // Simulate log "Started"
        await sagaStore.LogStepAsync(testCommand.SagaId.Value, starterMessageId, null, typeof(TestSagaCommand),
            StepStatus.Started, handlerType, testCommand, (SagaStepFailureInfo?)null);

        var finished = new TaskCompletionSource<bool>();

        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var (body, messageType, _, headers) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
                {
                    var (_, serCtx) = serializer.CreateContextFor(messageType);
                    var normalized = serializer.NormalizeTransportHeaders(headers);
                    if (serializer.Deserialize(body, normalized, serCtx) is not TestSagaCommand msg) continue;
                    receivedMessages.Add(msg);

                    // Log step as Failed on exception
                    if (msg.Message == "trigger-failure")
                    {
                        if (msg.SagaId is not { } sagaId)
                            throw new InvalidOperationException("A saga identifier is required for compensation.");

                        await sagaStore.LogStepAsync(sagaId, starterMessageId, null, typeof(TestSagaCommand),
                            StepStatus.Failed, handlerType, msg, (SagaStepFailureInfo?)null);
                        finished.TrySetResult(true);
                        throw new InvalidOperationException("Intentional failure for compensation.");
                    }
                }
            }
            catch
            {
                finished.TrySetResult(true); // Any exception (inc. cancellation) also ends the test
            }
        });

        // Wait for consumer infrastructure to be ready
        // ConsumeAsync sets up exchanges/queues lazily on first iteration
        await Task.Delay(3000);

        await eventBus.Send(testCommand);

        await Task.WhenAny(finished.Task, Task.Delay(15000, cts.Token));

        if (!finished.Task.IsCompleted)
        {
            throw new TimeoutException("TestSagaCommand was not processed in time!");
        }

        cts.Cancel();
        await consumerTask;


        await Task.Delay(2000);

        // Assert: Message should have been received
        receivedMessages.Should().ContainSingle(x => x.Message == "trigger-failure");

        // Assert: the Redis log should contain a failed step.
        var sagaSteps = await sagaStore.GetSagaHandlerStepsAsync(testCommand.SagaId.Value);
        sagaSteps.Should().Contain(x => x.Value.Status == StepStatus.Failed);

        await eventBus.DisposeAsync();
    }

    // Dummy saga command
    private interface ITestAppCommand : ICommand, ICommandEndpoint { }

    private class TestSagaCommand : CommandBase, ITestAppCommand
    {
        public string Message { get; set; } = string.Empty;
    }

    private sealed class ReplicaResponse : ResponseBase<TestSagaCommand>
    {
        public string Message { get; set; } = string.Empty;
    }

    private sealed class RoutedDummyChildEvent : DummyChildEvent, IRequestRoutingMetadata
    {
        public Guid RequestId { get; set; }
        public string? ResponseEndpoint { get; set; }

#pragma warning disable CS0618
        public string? ReplyTo
        {
            get => ResponseEndpoint;
            set => ResponseEndpoint = value;
        }
#pragma warning restore CS0618
    }

    // Dummy handler that always throws (simulates saga failure and compensation path)
    private class FailingSagaHandler : ReactiveSagaHandler<TestSagaCommand>
    {
        public override Task HandleAsync(TestSagaCommand message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Intentional failure for test.");
        }
    }
}

// Minimal dummy ISagaCompensationCoordinator implementation for testing.
internal class DummySagaCompensationCoordinator : ISagaCompensationCoordinator
{
    public Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message,
        SagaStepFailureInfo? failInfo, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
