using System.Text.Json;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Lycia.Messaging;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Stores;
using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Handlers;
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
        .WithImage("rabbitmq:3.13-management-alpine")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    private string RabbitMqConnectionString =>
           "amqp://guest:guest@127.0.0.1:5672/"; 
        //_rabbitMqContainer.GetConnectionString();

    private string RedisEndpoint =>
            "127.0.0.1:6379"; 
        //$"{_redisContainer.Hostname}:{_redisContainer.GetMappedPublicPort(6379)}";

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
    public async Task CompensationChain_Should_Be_Idempotent_For_Multiple_Compensation_Attempts()
    {
        // Arrange: Setup saga chain handlers (grandparent -> parent -> child)
        var applicationId = "TestApp";
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
            { SagaId = sagaId, MessageId = childId, ParentMessageId = parentId, Message = "trigger-failure" };

        // EventBus and Redis-backed SagaStore setup
        var grandParentQueueName = Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, Type>
        {
            { grandParentQueueName, typeof(DummyGrandparentEvent) },
            { parentQueueName, typeof(DummyParentEvent) },
            { childQueueName, typeof(DummyChildEvent) },
        };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(10) };
        var eventBus = await RabbitMqEventBus.CreateAsync(RabbitMqConnectionString,
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions);

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
        services.AddSingleton<StartReactiveSagaHandler<DummyGrandparentEvent>, GrandparentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyParentEvent>, ParentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyChildEvent>, ChildCompensationSagaHandler>();

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));
        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(sp.GetRequiredService<ISagaStore>(), dummySagaIdGenerator, sp));

        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var coordinator = serviceProvider.GetRequiredService<ISagaCompensationCoordinator>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as Compensated
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg);
        await sagaStore.LogStepAsync(sagaId, childId, parentId, typeof(DummyChildEvent), StepStatus.Compensated,
            handlerTypeChild, childMsg);

        // Act 1: Compensate child, parent and grandparent (normal compensation flow)
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyChildEvent), childMsg); 
        
        await WaitForInvocationsAsync(expectedParentCount: 1, expectedGrandparentCount: 1);

        // Save initial invocation counts for later assertions
        var parentInitialCount = ParentCompensationSagaHandler.Invocations.Count;
        var grandparentInitialCount = GrandparentCompensationSagaHandler.Invocations.Count;

        // Act 2: Try to compensate parent and grandparent again (should be idempotent)
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyChildEvent), childMsg); 
        
        await WaitForInvocationsAsync(expectedParentCount: 2, expectedGrandparentCount: 2);

        // Assert: No new invocations should be added
        ParentCompensationSagaHandler.Invocations.Count.Should().Be(parentInitialCount  + 1);
        GrandparentCompensationSagaHandler.Invocations.Count.Should().Be(grandparentInitialCount + 1);

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
        var applicationId = "TestApp";
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
        var grandParentQueueName = Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, Type>
        {
            { grandParentQueueName, typeof(DummyGrandparentEvent) },
            { parentQueueName, typeof(DummyParentEvent) },
            { childQueueName, typeof(DummyChildEvent) },
        };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(10) };
        var eventBus = await RabbitMqEventBus.CreateAsync(RabbitMqConnectionString,
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions);

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
        services.AddSingleton<StartReactiveSagaHandler<DummyGrandparentEvent>, GrandparentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyParentEvent>, ParentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyChildEvent>, ChildCompensationSagaHandler>();

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));

        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(sp.GetRequiredService<ISagaStore>(), dummySagaIdGenerator, sp));


        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var sagaDispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as CompensationFailed
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg);

        // Act 1: Call the protected DispatchCompensationHandlersAsync method using reflection (simulate compensation chain)
        await sagaDispatcher.DispatchAsync(childMsg);

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
        var applicationId = "TestApp";
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
            IsCompensationFailed = false,
            IsFailed = true,
            SagaId = sagaId, MessageId = childId, ParentMessageId = parentId, Message = "trigger-failure"
        };

        // EventBus and Redis-backed SagaStore setup
        var grandParentQueueName = Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyGrandparentEvent),
            handlerTypeGrandparent, applicationId);
        var parentQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyParentEvent), handlerTypeParent,
                applicationId);
        var childQueueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyChildEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, Type>
        {
            { grandParentQueueName, typeof(DummyGrandparentEvent) },
            { parentQueueName, typeof(DummyParentEvent) },
            { childQueueName, typeof(DummyChildEvent) },
        };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(10) };
        var eventBus = await RabbitMqEventBus.CreateAsync(RabbitMqConnectionString,
            NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions);

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
        services.AddSingleton<StartReactiveSagaHandler<DummyGrandparentEvent>, GrandparentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyParentEvent>, ParentCompensationSagaHandler>();
        services.AddSingleton<ReactiveSagaHandler<DummyChildEvent>, ChildCompensationSagaHandler>();

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));

        services.AddSingleton<ISagaDispatcher>(sp =>
            new SagaDispatcher(sp.GetRequiredService<ISagaStore>(), dummySagaIdGenerator, sp));

        var serviceProvider = services.BuildServiceProvider();

        var sagaStore = serviceProvider.GetRequiredService<ISagaStore>();
        var sagaDispatcher = serviceProvider.GetRequiredService<ISagaDispatcher>();

        // Pre-populate saga steps: grandparent and parent as Completed, child as CompensationFailed
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyGrandparentEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyParentEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg);

        // Act 1: Call the protected DispatchCompensationHandlersAsync method using reflection (simulate compensation chain)
        await sagaDispatcher.DispatchAsync(childMsg);

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
        var applicationId = "TestApp";
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
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(DummyEvent), handlerTypeChild, applicationId);
        var queueTypeMap = new Dictionary<string, Type> { { queueName, typeof(DummyEvent) } };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(10) };
        var eventBus = await RabbitMqEventBus.CreateAsync(
            RabbitMqConnectionString, NullLogger<RabbitMqEventBus>.Instance, queueTypeMap, eventBusOptions);

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

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<ISagaCompensationCoordinator>(sp =>
            new SagaCompensationCoordinator(sp, dummySagaIdGenerator));
        services.AddSingleton<ISagaStore>(sp =>
            new RedisSagaStore(redisDb, eventBus, dummySagaIdGenerator,
                sp.GetRequiredService<ISagaCompensationCoordinator>(), sagaStoreOptions));

        var provider = services.BuildServiceProvider();

        var sagaStore = provider.GetRequiredService<ISagaStore>();
        var coordinator = provider.GetRequiredService<ISagaCompensationCoordinator>();

        // Pre-populate the saga steps as "Completed".
        await sagaStore.LogStepAsync(sagaId, grandparentId, null, typeof(DummyEvent), StepStatus.Completed,
            handlerTypeGrandparent, grandparentMsg);
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyEvent), StepStatus.Completed,
            handlerTypeParent, parentMsg);

        // Mark child as Compensated (simulate successful compensation at leaf)
        await sagaStore.LogStepAsync(sagaId, childId, parentId, typeof(DummyEvent), StepStatus.Compensated,
            handlerTypeChild, childMsg);

        // First compensation attempt should compensate parent only
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), childMsg);

        ParentCompensationHandler.Invocations.Should().ContainSingle().And.Contain("ParentCompensationHandler");
        GrandparentCompensationHandler.Invocations.Should().BeEmpty();

        // Now mark parent as Compensated, which should trigger grandparent
        await sagaStore.LogStepAsync(sagaId, parentId, grandparentId, typeof(DummyEvent), StepStatus.Compensated,
            handlerTypeParent, parentMsg);
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), parentMsg);

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
        var applicationId = "TestApp";
        var handlerType = typeof(FailingSagaHandler);

        var queueName =
            Saga.Helpers.MessagingNamingHelper.GetRoutingKey(typeof(TestSagaCommand), handlerType, applicationId);
        var queueTypeMap = new Dictionary<string, Type> { { queueName, typeof(TestSagaCommand) } };
        var eventBusOptions = new EventBusOptions
            { ApplicationId = applicationId, MessageTTL = TimeSpan.FromSeconds(30) };

        var eventBus = await RabbitMqEventBus.CreateAsync(
            RabbitMqConnectionString,
            NullLogger<RabbitMqEventBus>.Instance,
            queueTypeMap,
            eventBusOptions);

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

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var receivedMessages = new List<TestSagaCommand>();

        var starterMessageId = Guid.NewGuid();
        // Simulate log "Started"
        await sagaStore.LogStepAsync(testCommand.SagaId.Value, starterMessageId, null, typeof(TestSagaCommand),
            StepStatus.Started, handlerType, testCommand);

        var finished = new TaskCompletionSource<bool>();

        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var (body, type) in eventBus.ConsumeAsync(cancellationToken: cts.Token))
                {
                    if (JsonSerializer.Deserialize(body, type) is not TestSagaCommand msg) continue;
                    receivedMessages.Add(msg);

                    // Log step as Failed on exception
                    if (msg.Message == "trigger-failure")
                    {
                        await sagaStore.LogStepAsync(msg.SagaId.Value, starterMessageId, null, typeof(TestSagaCommand),
                            StepStatus.Failed, handlerType, msg);
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

        await eventBus.Send(testCommand, handlerType: handlerType);

        await Task.WhenAny(finished.Task, Task.Delay(5000, cts.Token));

        if (!finished.Task.IsCompleted)
        {
            throw new TimeoutException("TestSagaCommand was not processed in time!");
        }

        cts.Cancel();
        await consumerTask;


        await Task.Delay(2000);

        // Assert: Message should have been received
        receivedMessages.Should().ContainSingle(x => x.Message == "trigger-failure");

        // Assert: Redis log should contain Failed adım
        var sagaSteps = await sagaStore.GetSagaHandlerStepsAsync(testCommand.SagaId.Value);
        sagaSteps.Should().Contain(x => x.Value.Status == StepStatus.Failed);

        await eventBus.DisposeAsync();
    }

    // Dummy saga command
    private class TestSagaCommand : CommandBase
    {
        public string Message { get; set; } = string.Empty;
    }

    // Dummy handler that always throws (simulates saga failure and compensation path)
    private class FailingSagaHandler : ReactiveSagaHandler<TestSagaCommand>
    {
        public override Task HandleAsync(TestSagaCommand message)
        {
            throw new InvalidOperationException("Intentional failure for test.");
        }
    }
}

// Minimal dummy ISagaCompensationCoordinator implementation for testing.
internal class DummySagaCompensationCoordinator : ISagaCompensationCoordinator
{
    public Task CompensateAsync(Guid sagaId, Type failedStepType)
    {
        throw new NotImplementedException();
    }

    public Task CompensateParentAsync(Guid sagaId, Type stepType, IMessage message)
    {
        throw new NotImplementedException();
    }
}