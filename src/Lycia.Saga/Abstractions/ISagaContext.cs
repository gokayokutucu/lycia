using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaContext<TInitialMessage>
    where TInitialMessage : IMessage
{
    Guid SagaId { get; }
    Type HandlerTypeOfCurrentStep { get; }
    ISagaStore SagaStore { get; }

    Task Send<T>(T command, CancellationToken ct = default) where T : ICommand;
    Task Publish<T>(T @event, CancellationToken ct = default) where T : IEvent;
    
    Task Publish<T>(T @event, Type? handlerType, CancellationToken ct = default) where T : IEvent;

    ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken ct = default)
        where TNextStep : IEvent;

    ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken ct = default)
        where TNextStep : ICommand;

    Task Compensate<T>(T @event, CancellationToken ct = default) where T : FailedEventBase;

    Task MarkAsComplete<TStep>(CancellationToken ct = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(CancellationToken ct = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex, CancellationToken ct = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken ct = default) where TStep : IMessage;
    Task MarkAsCompensated<TStep>(CancellationToken ct = default) where TStep : IMessage;
    Task CompensateAndBubbleUp<TStep>(CancellationToken ct = default) where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>(CancellationToken ct = default) where TStep : IMessage;    
    Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken ct = default) where TStep : IMessage;

    Task<bool> IsAlreadyCompleted<T>(CancellationToken ct = default) where T : IMessage;
    void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage;
}

public interface ISagaContext<TInitialMessage, TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    new ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken ct = default)
        where TNextStep : IEvent;

    new ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken ct = default)
        where TNextStep : ICommand;
}