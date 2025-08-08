using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaContext<TInitialMessage>
    where TInitialMessage : IMessage
{
    Guid SagaId { get; }
    Type HandlerTypeOfCurrentStep { get; }
    ISagaStore SagaStore { get; }

    Task Send<T>(T command) where T : ICommand;
    Task Publish<T>(T @event) where T : IEvent;
    
    Task Publish<T>(T @event, Type? handlerType) where T : IEvent;

    ReactiveSagaStepFluent<TInitialMessage> PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent;

    ReactiveSagaStepFluent<TInitialMessage> SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand;

    Task Compensate<T>(T @event) where T : FailedEventBase;

    Task MarkAsComplete<TStep>() where TStep : IMessage;
    Task MarkAsFailed<TStep>() where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail) where TStep : IMessage;
    Task MarkAsCompensated<TStep>() where TStep : IMessage;
    Task CompensateAndBubbleUp<TStep>() where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>() where TStep : IMessage;    
    Task MarkAsCompensationFailed<TStep>(Exception? ex) where TStep : IMessage;

    Task<bool> IsAlreadyCompleted<T>() where T : IMessage;
    void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage;
}

public interface ISagaContext<TInitialMessage, TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    new ICoordinatedSagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent;

    new ICoordinatedSagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand;
}