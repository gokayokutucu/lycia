using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaContext<TInitialMessage>
    where TInitialMessage : IMessage
{
    Guid SagaId { get; }
    Type HandlerTypeOfCurrentStep { get; }
    ISagaStore SagaStore { get; }

    Task Send<T>(T command, CancellationToken cancellationToken = default) where T : ICommand;
    Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent;
    
    Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent;

    ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken cancellationToken = default)
        where TNextStep : IEvent;

    ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken cancellationToken = default)
        where TNextStep : ICommand;

    Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : FailedEventBase;

    Task MarkAsComplete<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task CompensateAndBubbleUp<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;    
    Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCancelled<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;

    Task<bool> IsAlreadyCompleted<T>(CancellationToken cancellationToken = default) where T : IMessage;
    void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage;
}

public interface ISagaContext<TInitialMessage, TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    new ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken cancellationToken = default)
        where TNextStep : IEvent;

    new ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken cancellationToken = default)
        where TNextStep : ICommand;
}