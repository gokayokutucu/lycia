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

    ReactiveSagaStepFluent<TStep, TInitialMessage> PublishWithTracking<TStep>(TStep nextEvent)
        where TStep : IEvent;

    ReactiveSagaStepFluent<TStep, TInitialMessage> SendWithTracking<TStep>(TStep nextCommand)
        where TStep : ICommand;

    Task Compensate<T>(T @event) where T : FailedEventBase;

    Task MarkAsComplete<TStep>() where TStep : IMessage;
    Task MarkAsFailed<TStep>() where TStep : IMessage;
    Task MarkAsCompensated<TStep>() where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>() where TStep : IMessage;

    Task<bool> IsAlreadyCompleted<T>() where T : IMessage;
    void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage;
}

public interface ISagaContext<TInitialMessage, TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : new()
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    new CoordinatedSagaStepFluent<TStep, TSagaData> PublishWithTracking<TStep>(TStep nextEvent)
        where TStep : IEvent;

    new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep nextCommand)
        where TStep : ICommand;
}