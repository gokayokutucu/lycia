// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.Configurations;
using Lycia.Common.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Contexts;
using Lycia.Saga.Messaging.Handlers;
using Lycia.Tests.Messages;
using Lycia.Tests.SagaStates;
using Microsoft.Extensions.Options;
using Moq;

namespace Lycia.Tests;

/// <summary>
/// Regression coverage for a token-propagation bug found while auditing the saga context stack: the
/// protected <c>MarkAsComplete(cancellationToken)</c> and <c>MarkAsCompensationFailed(cancellationToken)</c>
/// convenience wrappers on every saga handler base class accepted a token but never passed it to the
/// underlying <c>Context</c> call - only <c>MarkAsFailed(cancellationToken)</c> did. Every base class is
/// fixed; this covers a representative reactive and coordinated handler through the same public
/// <c>Initialize(...)</c> entry point the dispatcher itself uses.
/// </summary>
public class HandlerBaseClassCancellationTests
{
    private sealed class ProbeReactiveHandler : ReactiveSagaHandler<DummyEvent>
    {
        public override Task HandleAsync(DummyEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CallMarkAsComplete(CancellationToken cancellationToken) => MarkAsComplete(cancellationToken);
        public Task CallMarkAsCompensationFailed(CancellationToken cancellationToken) => MarkAsCompensationFailed(cancellationToken);
    }

    private sealed class ProbeCoordinatedHandler : CoordinatedSagaHandler<DummyEvent, CreateOrderSagaData>
    {
        public override Task HandleAsync(DummyEvent message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CallMarkAsComplete(CancellationToken cancellationToken) => MarkAsComplete(cancellationToken);
        public Task CallMarkAsCompensationFailed(CancellationToken cancellationToken) => MarkAsCompensationFailed(cancellationToken);
    }

    private static IOptions<SagaOptions> DefaultSagaOptions => Options.Create(new SagaOptions());

    [Fact]
    public async Task Reactive_MarkAsComplete_Wrapper_Forwards_The_Token()
    {
        var sagaStoreMock = new Mock<ISagaStore>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        IMessage currentStep = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.NewGuid() };
        ISagaContext<IMessage> context = new SagaContext<IMessage>(Guid.NewGuid(), currentStep, typeof(ProbeReactiveHandler),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(), Mock.Of<ISagaCompensationCoordinator>());
        var handler = new ProbeReactiveHandler();
        handler.Initialize(context, DefaultSagaOptions);
        using var cts = new CancellationTokenSource();

        await handler.CallMarkAsComplete(cts.Token);

        sagaStoreMock.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
            StepStatus.Completed, It.IsAny<Type>(), It.IsAny<object?>(), (Exception?)null, cts.Token), Times.Once);
    }

    [Fact]
    public async Task Reactive_MarkAsCompensationFailed_Wrapper_Forwards_The_Token()
    {
        var sagaStoreMock = new Mock<ISagaStore>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        IMessage currentStep = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.NewGuid() };
        ISagaContext<IMessage> context = new SagaContext<IMessage>(Guid.NewGuid(), currentStep, typeof(ProbeReactiveHandler),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(), Mock.Of<ISagaCompensationCoordinator>());
        var handler = new ProbeReactiveHandler();
        handler.Initialize(context, DefaultSagaOptions);
        using var cts = new CancellationTokenSource();

        await handler.CallMarkAsCompensationFailed(cts.Token);

        sagaStoreMock.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
            StepStatus.CompensationFailed, It.IsAny<Type>(), It.IsAny<object?>(), (Exception?)null, cts.Token), Times.Once);
    }

    [Fact]
    public async Task Coordinated_MarkAsComplete_Wrapper_Forwards_The_Token()
    {
        var sagaStoreMock = new Mock<ISagaStore>();
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock.SetupGet(b => b.ApplicationId).Returns("TestApp");
        IMessage currentStep = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.NewGuid() };
        ISagaContext<IMessage, CreateOrderSagaData> context = new SagaContext<IMessage, CreateOrderSagaData>(
            Guid.NewGuid(), currentStep, typeof(ProbeCoordinatedHandler), new CreateOrderSagaData(),
            eventBusMock.Object, sagaStoreMock.Object, Mock.Of<ISagaIdGenerator>(), Mock.Of<ISagaCompensationCoordinator>());
        var handler = new ProbeCoordinatedHandler();
        handler.Initialize(context, DefaultSagaOptions);
        using var cts = new CancellationTokenSource();

        await handler.CallMarkAsComplete(cts.Token);

        sagaStoreMock.Verify(s => s.SaveSagaDataAsync(It.IsAny<Guid>(), It.IsAny<CreateOrderSagaData>(), cts.Token), Times.Once);
        sagaStoreMock.Verify(s => s.LogStepAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Type>(),
            StepStatus.Completed, It.IsAny<Type>(), It.IsAny<object?>(), (Exception?)null, cts.Token), Times.Once);
    }
}
