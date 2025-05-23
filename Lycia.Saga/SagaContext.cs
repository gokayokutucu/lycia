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
        var task = Publish(@event);
        var context = new SagaContext<TStep, TSagaData>(SagaId, Data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(context, task);
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        var task = Send(command);
        var context = new SagaContext<TStep, TSagaData>(SagaId, Data, _eventBus, _sagaStore, _sagaIdGenerator);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(context, task);
    }
}