using System.Collections.Concurrent;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga;

public class SagaContext<TMessage>(
    Guid sagaId,
    Type handlerType,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator) : ISagaContext<TMessage>
    where TMessage : IMessage
{
    protected readonly ConcurrentDictionary<Type, IMessage> _stepMessages = new();
    public ISagaStore SagaStore { get; } = sagaStore;
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;
    public Type HandlerType { get; } = handlerType;

    public Task Send<T>(T command) where T : ICommand
    {
        _stepMessages[typeof(T)] = command;
        return eventBus.Send(command, SagaId);
    }


    public Task Publish<T>(T @event) where T : IEvent
    {
        _stepMessages[typeof(T)] = @event;
        return eventBus.Publish(@event, SagaId);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep @event) where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
       // @event.SetParentMessageId(typeof(TMessage));
        _stepMessages[typeof(TStep)] = @event;
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, @event);
        Task Operation() => Publish(@event);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        command.SetSagaId(SagaId);
      //  command.SetParentMessageId(typeof(TMessage));
        _stepMessages[typeof(TStep)] = command;
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, command);
        Task Operation() => Send(command);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        _stepMessages[typeof(T)] = @event;
        return eventBus.Publish(@event, SagaId);
    }

    public Task MarkAsComplete<TStep>() where TStep : IMessage
    {
        _stepMessages.TryGetValue(typeof(TStep), out var msg);
        return SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Completed, HandlerType, msg);
    }


    public async Task MarkAsFailed<TStep>() where TStep : IMessage
    {
        _stepMessages.TryGetValue(typeof(TStep), out var msg);
        await SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Failed, HandlerType, msg);
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep));
    }

    public async Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        _stepMessages.TryGetValue(typeof(TStep), out var msg);
        await SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Compensated, HandlerType, msg);
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep));
    }

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage
    {
        _stepMessages.TryGetValue(typeof(TStep), out var msg);
        return SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.CompensationFailed, HandlerType, msg);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage =>
        SagaStore.IsStepCompletedAsync(SagaId, typeof(T), HandlerType);

    /// <summary>
    /// Registers a step message by storing it in the internal step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessage1">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessage1>(TMessage1 message) where TMessage1 : IMessage
    {
        _stepMessages[typeof(TMessage1)] = message;
    }
}

public class SagaContext<TMessage, TSagaData>(
    Guid sagaId,
    Type handlerType,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator)
    : SagaContext<TMessage>(sagaId, handlerType, eventBus, sagaStore, sagaIdGenerator, compensationCoordinator),
        ISagaContext<TMessage, TSagaData>
    where TSagaData : SagaData
    where TMessage : IMessage
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ISagaStore _sagaStore = sagaStore;

    public TSagaData Data { get; } = data;

    public new CoordinatedSagaStepFluent<TStep, TSagaData> PublishWithTracking<TStep>(TStep @event)
        where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
        _stepMessages[typeof(TStep)] = @event;
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, HandlerType, Data, _eventBus, _sagaStore, _stepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, @event);

        Task Operation() => Publish(@event); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        _stepMessages[typeof(TStep)] = command;
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, HandlerType, Data, _eventBus, _sagaStore, _stepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, command);

        Task Operation() => Send(command); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TStepAdapter, TSagaDataAdapter>(
    Guid sagaId,
    Type handlerType,
    TSagaDataAdapter data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ConcurrentDictionary<Type, IMessage> stepMessages)
    : ISagaContext<TStepAdapter, TSagaDataAdapter>
    where TStepAdapter : IMessage
    where TSagaDataAdapter : SagaData
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerType { get; } = handlerType;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand
    {
        stepMessages[typeof(T)] = command;
        return eventBus.Send(command, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[typeof(T)] = @event;
        return eventBus.Publish(@event, sagaId);
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        stepMessages[typeof(T)] = @event;
        return eventBus.Publish(@event, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue(typeof(TMarkStep), out var msg);
        return sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Completed, HandlerType, msg);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue(typeof(TMarkStep), out var msg);
        return sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Failed, HandlerType, msg);
    }

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue(typeof(TMarkStep), out var msg);
        return sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Compensated, HandlerType, msg);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue(typeof(TMarkStep), out var msg);
        return sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.CompensationFailed, HandlerType, msg);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.IsStepCompletedAsync(sagaId, typeof(TMarkStep), HandlerType);

    /// <summary>
    /// Registers a step message by storing it in the step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage
    {
        stepMessages[typeof(TMessage)] = message;
    }

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep @event)
    {
        @event.SetSagaId(SagaId);
        stepMessages[typeof(TReactiveStep)] = @event;
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep command)
    {
        command.SetSagaId(SagaId);
        stepMessages[typeof(TReactiveStep)] = command;
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep @event)
        where TNewStep : IEvent
    {
        @event.SetSagaId(SagaId);
        stepMessages[typeof(TNewStep)] = @event;
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, HandlerType, data, eventBus,
                sagaStore, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep command)
        where TNewStep : ICommand
    {
        command.SetSagaId(SagaId);
        stepMessages[typeof(TNewStep)] = command;
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, HandlerType, data, eventBus,
                sagaStore, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }
}