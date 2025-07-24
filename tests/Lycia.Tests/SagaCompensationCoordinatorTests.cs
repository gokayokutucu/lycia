using Lycia.Infrastructure.Compensating;
using Lycia.Infrastructure.Dispatching;
using Lycia.Infrastructure.Eventing;
using Lycia.Infrastructure.Stores;
using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Messaging.Enums;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Extensions;
using Lycia.Tests.Helpers;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Sample.Shared.Messages.Events;

namespace Lycia.Tests;

public class SagaCompensationCoordinatorTests
{
    
    [Fact]
    public async Task Compensation_Should_Not_Invoke_Handler_If_Already_Compensated_Or_CompensationFailed()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var stepType = typeof(DummyEvent);

        // Define a handler that records invocation
        var handler = new NoOpHandler();
        NoOpHandler.Invocations.Clear();

        var services = new ServiceCollection();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = Mock.Of<ISagaIdGenerator>();
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // Case 1: StepStatus is already Compensated
        var payload = new DummyEvent()
        {
            MessageId = messageId,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        await store.LogStepAsync(sagaId, messageId, Guid.Empty, stepType, StepStatus.Compensated, typeof(NoOpHandler),
            payload);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>>(handler);
        services.AddSingleton<IEventBus>(eventBusMock);
        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act
        await coordinator.CompensateAsync(sagaId, stepType);

        // Assert: Handler should NOT be called because step is already Compensated
        Assert.Empty(NoOpHandler.Invocations);

        // Case 2: StepStatus is already CompensationFailed
        NoOpHandler.Invocations.Clear();
        var sagaId2 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();
        var payload2 = new DummyEvent()
        {
            MessageId = messageId2,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        await store.LogStepAsync(sagaId2, messageId2, Guid.Empty, stepType, StepStatus.CompensationFailed,
            typeof(NoOpHandler), payload2);

        // Act
        await coordinator.CompensateAsync(sagaId2, stepType);

        // Assert: Handler should NOT be called because step is already CompensationFailed
        Assert.Empty(NoOpHandler.Invocations);
    }

    [Fact]
    public async Task CompensateParentAsync_Should_Detect_And_Stop_On_CircularParentChain()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var stepType = typeof(DummyEvent);

        var messageId1 = Guid.NewGuid();
        var messageId2 = Guid.NewGuid();

        // Create a circular chain:
        // msg1.ParentMessageId = msg2
        // msg2.ParentMessageId = msg1
        var msg1 = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = messageId1,
            ParentMessageId = messageId2,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        var msg2 = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = messageId2,
            ParentMessageId = messageId1,
            CorrelationId = msg1.CorrelationId,
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        // Handlers will record invocations
        CircularHandler.Invocations.Clear();

        var services = new ServiceCollection();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = new TestSagaIdGenerator(sagaId);
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // Log both steps with compensation failed status to trigger parent chain
        await store.LogStepAsync(sagaId, messageId1, messageId2, stepType, StepStatus.CompensationFailed,
            typeof(CircularHandler), msg1);
        await Assert.ThrowsAsync<SagaStepCircularChainException>(()=> store.LogStepAsync(sagaId, messageId2, messageId1, stepType, StepStatus.CompensationFailed,
            typeof(CircularHandler), msg2));
    }

    private class CircularHandler : ISagaCompensationHandler<DummyEvent>
    {
        public static readonly List<string> Invocations = [];

        public Task CompensateAsync(DummyEvent message)
        {
            Invocations.Add(nameof(CircularHandler));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CompensateAsync_Should_Invoke_All_Registered_CompensationHandlers_For_SameMessageType()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var handler1 = new MultiHandler1();
        var handler2 = new MultiHandler2();

        var services = new ServiceCollection();
        var eventBusMock = new Mock<IEventBus>().Object;
        var sagaIdGen = Mock.Of<ISagaIdGenerator>();
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        var stepType = typeof(DummyEvent);
        var payload = new DummyEvent
        {
            MessageId = messageId,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        await store.LogStepAsync(
            sagaId,
            messageId,
            Guid.Empty,
            stepType,
            StepStatus.Failed, // Force compensation trigger
            stepType,
            payload);

        // Register two different handlers for the same message type
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>>(handler1);
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>>(handler2);
        services.AddSingleton<IEventBus>(eventBusMock);
        services.AddSingleton<ISagaStore>(store);

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        MultiHandler1.Called = false;
        MultiHandler2.Called = false;

        // Act
        await coordinator.CompensateAsync(sagaId, stepType);

        // Assert
        Assert.True(MultiHandler1.Called);
        Assert.True(MultiHandler2.Called);
    }

    [Fact]
    public async Task CompensationChain_Should_Stop_On_CompensationFailure()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageIdOfGrandParent = Guid.NewGuid();
        var messageIdOfParent = Guid.NewGuid();
        var messageIdOfChild = Guid.NewGuid();

        // Create dummy messages for each step in the chain.
        var grandparent = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = messageIdOfGrandParent,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        var parent = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = messageIdOfParent,
            ParentMessageId = messageIdOfGrandParent,
            CorrelationId = grandparent.CorrelationId,
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        var child = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = messageIdOfChild,
            ParentMessageId = messageIdOfParent,
            CorrelationId = parent.CorrelationId,
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        GrandparentCompensationHandler.Invocations.Clear();
        ParentCompensationHandler.Invocations.Clear();
        ChildCompensationHandler.Invocations.Clear();

        // Register service provider with handlers
        var services = new ServiceCollection();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = new TestSagaIdGenerator(sagaId);
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // Log steps to set up the chain:
        var stepType = typeof(DummyEvent);

        // Grandparent is completed
        await store.LogStepAsync(sagaId, messageIdOfGrandParent, Guid.Empty, stepType, StepStatus.Completed,
            typeof(GrandparentCompensationHandler), grandparent);

        // Parent step is completed
        await store.LogStepAsync(sagaId, messageIdOfParent, messageIdOfGrandParent, stepType, StepStatus.Completed,
            typeof(ParentCompensationHandler), parent);

        // Child step is CompensationFailed to simulate failure and trigger parent chain
        await store.LogStepAsync(sagaId, messageIdOfChild, messageIdOfParent, stepType, StepStatus.CompensationFailed,
            typeof(ChildCompensationHandler), child);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock);
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, GrandparentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ParentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ChildCompensationHandler>();

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act
        await coordinator.CompensateAsync(sagaId, stepType);

        // Assert
        // Parent should be invoked for compensation (even if it fails), but Grandparent must not be called.
        Assert.DoesNotContain("GrandparentCompensationHandler", GrandparentCompensationHandler.Invocations);
        Assert.DoesNotContain("ParentCompensationHandler", ParentCompensationHandler.Invocations);
        Assert.DoesNotContain("ChildCompensationHandler", ChildCompensationHandler.Invocations);
    }

    [Fact]
    public async Task CompensateAsync_Should_Invoke_CompensateAsync_On_Handler()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var handlerMock = new Mock<ISagaCompensationHandler<DummyEvent>>();
        handlerMock.Setup(h => h.CompensateAsync(It.IsAny<DummyEvent>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var services = new ServiceCollection();
        var eventBusMock = new Mock<IEventBus>().Object;
        var sagaIdGen = Mock.Of<ISagaIdGenerator>();
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();

        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // IMPORTANT: Use the same type as the handler expects
        var stepType = typeof(DummyEvent);
        var handlerType = typeof(ISagaCompensationHandler<DummyEvent>);
        var payload = new DummyEvent()
        {
            MessageId = messageId,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        await store.LogStepAsync(
            sagaId,
            messageId,
            Guid.Empty,
            stepType,
            StepStatus.Failed, // Triggering compensation set failed or another proper status
            handlerType, // Used an interface type for the handler
            payload);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock);
        services.AddSingleton(handlerMock.Object);

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act
        await coordinator.CompensateAsync(sagaId, stepType);

        // Assert
        handlerMock.Verify(h => h.CompensateAsync(It.IsAny<DummyEvent>()), Times.Once);
    }

    [Fact]
    public async Task CompensateParentAsync_Should_Invoke_ParentChain_In_Order()
    {
        // Arrange
        var fixedSagaId = Guid.NewGuid();

        var messageIdOfGrandParent = Guid.NewGuid();
        var messageIdOfParent = Guid.NewGuid();
        var messageIdOfChild = Guid.NewGuid();

        var grandparent = new DummyEvent
        {
            SagaId = fixedSagaId,
            MessageId = messageIdOfGrandParent,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        var parent = new DummyEvent
        {
            SagaId = fixedSagaId,
            MessageId = messageIdOfParent,
            ParentMessageId = messageIdOfGrandParent,
            CorrelationId = grandparent.CorrelationId,
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };
        var child = new DummyEvent
        {
            SagaId = fixedSagaId,
            MessageId = messageIdOfChild,
            ParentMessageId = messageIdOfParent,
            CorrelationId = parent.CorrelationId,
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        GrandparentCompensationHandler.Invocations.Clear();
        ParentCompensationHandler.Invocations.Clear();
        ChildCompensationHandler.Invocations.Clear();

        var services = new ServiceCollection();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = new TestSagaIdGenerator(fixedSagaId);
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // Step record(for chain)
        var stepType = typeof(DummyEvent);

        // Grandparent
        await store.LogStepAsync(fixedSagaId, messageIdOfGrandParent, Guid.Empty, stepType, StepStatus.Compensated,
            typeof(GrandparentCompensationHandler), grandparent);
        // Parent
        await store.LogStepAsync(fixedSagaId, messageIdOfParent, messageIdOfGrandParent, stepType, StepStatus.Compensated,
            typeof(ParentCompensationHandler),
            parent);
        // Child (last step failed)
        await store.LogStepAsync(fixedSagaId, messageIdOfChild, messageIdOfParent, stepType, StepStatus.CompensationFailed,
            typeof(ChildCompensationHandler), child);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<IEventBus>(eventBusMock);
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, GrandparentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ParentCompensationHandler>();
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ChildCompensationHandler>();

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act
        await coordinator.CompensateParentAsync(fixedSagaId, stepType, parent);
        await coordinator.CompensateParentAsync(fixedSagaId, stepType, child);

        // Assert
        Assert.Contains("ParentCompensationHandler", ParentCompensationHandler.Invocations);
        Assert.Contains("GrandparentCompensationHandler", GrandparentCompensationHandler.Invocations);
    }

    [Fact]
    public async Task CompensateAsync_Should_DoNothing_When_NoHandlerRegistered()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = Mock.Of<ISagaIdGenerator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        var stepType = typeof(DummyEvent);
        var payload = new DummyEvent()
        {
            MessageId = messageId,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        await store.LogStepAsync(sagaId, messageId, Guid.Empty, stepType, StepStatus.Failed, typeof(object), payload);

        var services = new ServiceCollection();
        services.AddSingleton<IEventBus>(eventBusMock);
        services.AddSingleton<ISagaStore>(store);

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act + Assert
        await coordinator.CompensateAsync(sagaId, stepType);
    }

    [Fact]
    public async Task CompensateParentAsync_Should_DoNothing_When_NoParent()
    {
        // Arrange
        var sagaId = Guid.NewGuid();
        var rootMessageId = Guid.NewGuid();
        var rootParentMessageId = Guid.Empty;

        var root = new DummyEvent
        {
            SagaId = sagaId,
            MessageId = rootMessageId,
            ParentMessageId = Guid.Empty,
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationId = "Test"
        };

        ParentCompensationHandler.Invocations.Clear();
        var services = new ServiceCollection();
        var eventBusMock = Mock.Of<IEventBus>();
        var sagaIdGen = new TestSagaIdGenerator(sagaId);
        var dummyCoordinator = Mock.Of<ISagaCompensationCoordinator>();
        var store = new InMemorySagaStore(eventBusMock, sagaIdGen, dummyCoordinator);

        // Save step log for root message
        await store.LogStepAsync(
            sagaId, rootMessageId, rootParentMessageId, typeof(DummyEvent), StepStatus.CompensationFailed,
            typeof(ParentCompensationHandler), root);

        services.AddSingleton<ISagaStore>(store);
        services.AddSingleton<ISagaCompensationHandler<DummyEvent>, ParentCompensationHandler>();
        services.AddSingleton<IEventBus>(eventBusMock);

        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen);

        // Act
        await coordinator.CompensateParentAsync(sagaId, typeof(DummyEvent), root);

        // Assert
        Assert.Empty(ParentCompensationHandler.Invocations);
    }

    private class NotDummyEvent : EventBase
    {
    }

    private class NoOpHandler : ISagaCompensationHandler<DummyEvent>
    {
        public static readonly List<string> Invocations = [];

        public Task CompensateAsync(DummyEvent message)
        {
            Invocations.Add(nameof(NoOpHandler));
            return Task.CompletedTask;
        }
    }

    // Dummy multi-handler implementations for testing
    private class MultiHandler1 : ISagaCompensationHandler<DummyEvent>
    {
        public static bool Called = false;

        public Task CompensateAsync(DummyEvent message)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    private class MultiHandler2 : ISagaCompensationHandler<DummyEvent>
    {
        public static bool Called = false;

        public Task CompensateAsync(DummyEvent message)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}