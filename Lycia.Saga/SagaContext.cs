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
        var task = Publish(@event); // This calls the base SagaContext<TMessage>.Publish
        var context = new StepSpecificSagaContextAdapter<TStep, TSagaData>(this.SagaId, this.Data, this._eventBus, this._sagaStore, this._sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(context, task);
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        var task = Send(command); // This calls the base SagaContext<TMessage>.Send
        var context = new StepSpecificSagaContextAdapter<TStep, TSagaData>(this.SagaId, this.Data, this._eventBus, this._sagaStore, this._sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(context, task);
    }
}

// Private adapter class
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
    public Guid SagaId => sagaId;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand => eventBus.Send(command, sagaId);
    public Task Publish<T>(T @event) where T : IEvent => eventBus.Publish(@event, sagaId);
    public Task Compensate<T>(T @event) where T : FailedEventBase => eventBus.Publish(@event, sagaId);

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Completed);
    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Failed);
    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.Compensated);
    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage => sagaStore.LogStepAsync(sagaId, typeof(TMarkStep), StepStatus.CompensationFailed);
    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage => sagaStore.IsStepCompletedAsync(sagaId, typeof(TMarkStep));

    // Fluent Tracking Methods for ISagaContext<TStepAdapter> (base interface part)
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(TReactiveStep @event)
    {
        var task = Publish(@event);
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, task);
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(TReactiveStep command)
    {
        var task = Send(command);
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, task);
    }

    // Fluent Tracking Methods for ISagaContext<TStepAdapter, TSagaDataAdapter> (the 'new' methods)
    public CoordinatedSagaStepFluent<TCoordStep, TSagaDataAdapter> PublishWithTracking<TCoordStep>(TCoordStep @event) where TCoordStep : IEvent
    {
        var task = Publish(@event);
        var stepContextForCoordFluent = new StepSpecificSagaContextAdapter<TCoordStep, TSagaDataAdapter>(sagaId, data, eventBus, sagaStore, sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TCoordStep, TSagaDataAdapter>(stepContextForCoordFluent, task);
    }

    public CoordinatedSagaStepFluent<TCoordStep, TSagaDataAdapter> SendWithTracking<TCoordStep>(TCoordStep command) where TCoordStep : ICommand
    {
        var task = Send(command);
        var stepContextForCoordFluent = new StepSpecificSagaContextAdapter<TCoordStep, TSagaDataAdapter>(sagaId, data, eventBus, sagaStore, sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TCoordStep, TSagaDataAdapter>(stepContextForCoordFluent, task);
    }
}