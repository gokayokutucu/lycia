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
    ISagaIdGenerator sagaIdGenerator) : ISagaContext<TMessage>
    where TMessage : IMessage
{
    public ISagaStore SagaStore { get; } = sagaStore;
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;
    public Type HandlerType { get; } = handlerType;

    public Task Send<T>(T command) where T : ICommand =>
        eventBus.Send(command, SagaId);

    public Task Publish<T>(T @event) where T : IEvent =>
        eventBus.Publish(@event, SagaId);

    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep @event) where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, @event);
        Task Operation() => Publish(@event);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, command);
        Task Operation() => Send(command);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, SagaId);
    }

    public Task MarkAsComplete<TStep>() where TStep : IMessage =>
        SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Completed, HandlerType);

    public Task MarkAsFailed<TStep>() where TStep : IMessage =>
        SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Failed, HandlerType);

    public Task MarkAsCompensated<TStep>() where TStep : IMessage =>
        SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Compensated, HandlerType);

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage =>
        SagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.CompensationFailed, HandlerType);

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage =>
        SagaStore.IsStepCompletedAsync(SagaId, typeof(T), HandlerType);
}

public class SagaContext<TMessage, TSagaData>(
    Guid sagaId,
    Type handlerType,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator)
    : SagaContext<TMessage>(sagaId, handlerType, eventBus, sagaStore, sagaIdGenerator), ISagaContext<TMessage, TSagaData>
    where TSagaData : SagaData
    where TMessage : IMessage
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ISagaStore _sagaStore = sagaStore;
    
    private readonly ISagaIdGenerator _sagaIdGenerator = sagaIdGenerator;
    public TSagaData Data { get; } = data;

    public new CoordinatedSagaStepFluent<TStep, TSagaData> PublishWithTracking<TStep>(TStep @event)
        where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, HandlerType, Data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, @event);

        Task Operation() => Publish(@event); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, HandlerType, Data, _eventBus, _sagaStore, _sagaIdGenerator);
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
    ISagaIdGenerator sagaIdGenerator)
    : ISagaContext<TStepAdapter, TSagaDataAdapter>
    where TStepAdapter : IMessage
    where TSagaDataAdapter : SagaData
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerType { get; } = handlerType;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand => eventBus.Send(command, sagaId);
    public Task Publish<T>(T @event) where T : IEvent => eventBus.Publish(@event, sagaId);

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Completed, HandlerType);

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Failed, HandlerType);

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Compensated, HandlerType);

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.CompensationFailed, HandlerType);

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage =>
        sagaStore.IsStepCompletedAsync(sagaId, typeof(TMarkStep), HandlerType);

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep @event)
    {
        @event.SetSagaId(SagaId);
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep command)
    {
        command.SetSagaId(SagaId);
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep @event)
        where TNewStep : IEvent
    {
        @event.SetSagaId(SagaId);
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, HandlerType, data, eventBus, sagaStore,
                sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep command)
        where TNewStep : ICommand
    {
        command.SetSagaId(SagaId);
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, HandlerType, data, eventBus, sagaStore,
                sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }
}