using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga;

public class SagaContext<TMessage>(
    Guid sagaId,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator) : ISagaContext<TMessage>
    where TMessage : IMessage
{
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;

    public Task Send<T>(T command) where T : ICommand =>
        eventBus.Send(command, SagaId);

    public Task Publish<T>(T @event) where T : IEvent =>
        eventBus.Publish(@event, SagaId);

    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep @event) where TStep : IEvent
    {
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, @event);
        Task Operation() => Publish(@event);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, command);
        Task Operation() => Send(command);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase =>
        eventBus.Publish(@event, SagaId);

    public Task MarkAsComplete<TStep>() where TStep : IMessage =>
        sagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Completed);

    public Task MarkAsFailed<TStep>() where TStep : IMessage =>
        sagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Failed);

    public Task MarkAsCompensated<TStep>() where TStep : IMessage =>
        sagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.Compensated);

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage =>
        sagaStore.LogStepAsync(SagaId, typeof(TStep), StepStatus.CompensationFailed);

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage =>
        sagaStore.IsStepCompletedAsync(SagaId, typeof(T));
}

public class SagaContext<TMessage, TSagaData>(
    Guid sagaId,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator)
    : SagaContext<TMessage>(sagaId, eventBus, sagaStore, sagaIdGenerator), ISagaContext<TMessage, TSagaData>
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
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext = new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, Data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, @event);
        
        Task Operation() => Publish(@event);// Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        var adapterContext = new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, Data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, command);
        
        Task Operation() => Send(command); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TStepAdapter, TSagaDataAdapter>(
    Guid sagaId,
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
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand => eventBus.Send(command, sagaId);
    public Task Publish<T>(T @event) where T : IEvent => eventBus.Publish(@event, sagaId);
    
    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase => eventBus.Publish(@event, sagaId);

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Completed);
    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Failed);
    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Compensated);
    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.CompensationFailed);
    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage => sagaStore.IsStepCompletedAsync(sagaId, typeof(TMarkStep));

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(TReactiveStep @event)
    {
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, @event);
        
        Task Operation() => Publish(@event);  // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(TReactiveStep command)
    {
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, command);
        
        Task Operation() => Send(command); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep @event) where TNewStep : IEvent
    {
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext = new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, data, eventBus, sagaStore, sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, @event);
        
        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep command) where TNewStep : ICommand
    {
        var nextStepContext = new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, data, eventBus, sagaStore, sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, command);
        
        Task Operation() => Send(command); // Calls this adapter's Send
    }
}