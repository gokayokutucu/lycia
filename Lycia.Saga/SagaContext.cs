using Lycia.Messaging;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Enums;
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
        var task = Publish(@event);
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, task);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        var task = Send(command);
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, task);
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
        var task = base.Publish(@event); // Explicitly call base to ensure it's the intended IEventBus.Publish
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext = new StepSpecificSagaContextAdapter<TStep, TSagaData>(this.SagaId, this.Data, this._eventBus, this._sagaStore, this._sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, task);
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        var task = base.Send(command); // Explicitly call base
        var adapterContext = new StepSpecificSagaContextAdapter<TStep, TSagaData>(this.SagaId, this.Data, this._eventBus, this._sagaStore, this._sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, task);
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TStepAdapter, TSagaDataAdapter> : ISagaContext<TStepAdapter, TSagaDataAdapter>
    where TStepAdapter : IMessage
    where TSagaDataAdapter : SagaData
{
    private readonly Guid _sagaId;
    private readonly TSagaDataAdapter _data;
    private readonly IEventBus _eventBus;
    private readonly ISagaStore _sagaStore;
    private readonly ISagaIdGenerator _sagaIdGenerator;

    public StepSpecificSagaContextAdapter(Guid sagaId, TSagaDataAdapter data, IEventBus eventBus, ISagaStore sagaStore, ISagaIdGenerator sagaIdGenerator)
    {
        _sagaId = sagaId;
        _data = data;
        _eventBus = eventBus;
        _sagaStore = sagaStore;
        _sagaIdGenerator = sagaIdGenerator; // Not used by ISagaContext methods but part of constructor signature
    }

    public Guid SagaId => _sagaId;
    public TSagaDataAdapter Data => _data;

    public Task Send<T>(T command) where T : ICommand => _eventBus.Send(command, _sagaId);
    public Task Publish<T>(T @event) where T : IEvent => _eventBus.Publish(@event, _sagaId);
    
    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase => _eventBus.Publish(@event, _sagaId);

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage => _sagaStore.LogStepAsync(_sagaId, typeof(TMarkStep), StepStatus.Completed);
    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage => _sagaStore.LogStepAsync(_sagaId, typeof(TMarkStep), StepStatus.Failed);
    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage => _sagaStore.LogStepAsync(_sagaId, typeof(TMarkStep), StepStatus.Compensated);
    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage => _sagaStore.LogStepAsync(_sagaId, typeof(TMarkStep), StepStatus.CompensationFailed);
    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage => _sagaStore.IsStepCompletedAsync(_sagaId, typeof(TMarkStep));

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(TReactiveStep @event)
    {
        var task = Publish(@event); // Calls this adapter's Publish
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, task);
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(TReactiveStep command)
    {
        var task = Send(command); // Calls this adapter's Send
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, task);
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep @event) where TNewStep : IEvent
    {
        var task = Publish(@event); // Calls this adapter's Publish
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext = new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(_sagaId, _data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, task);
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep command) where TNewStep : ICommand
    {
        var task = Send(command); // Calls this adapter's Send
        var nextStepContext = new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(_sagaId, _data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, task);
    }
}