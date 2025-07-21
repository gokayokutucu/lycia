using System.Collections.Concurrent;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga;

public class SagaContext<TMessage>(
    Guid sagaId,
    TMessage currentContextMessage,
    Type handlerType,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator) : ISagaContext<TMessage>
    where TMessage : IMessage
{
    protected readonly ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> StepMessages = new();
    public ISagaStore SagaStore { get; } = sagaStore;
    public TMessage CurrentContextMessage { get; } = currentContextMessage;
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;
    public Type HandlerType { get; } = handlerType;
    
    /// <summary>
    /// Registers a step message by storing it in the internal step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessage1">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessage1>(TMessage1 message) where TMessage1 : IMessage
    {
        StepMessages[(typeof(TMessage1), message.MessageId)] = message;
    }

    public Task Send<T>(T command) where T : ICommand
    {
        StepMessages[(typeof(T), command.MessageId)] = command;
        return eventBus.Send(command, HandlerType, SagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerType, SagaId);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep @event) where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
        @event.SetParentMessageId(CurrentContextMessage.MessageId);
        StepMessages[(typeof(TStep), @event.MessageId)] = @event;
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, CurrentContextMessage);
        Task Operation() => Publish(@event);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(CurrentContextMessage.MessageId);
        StepMessages[(typeof(TStep), command.MessageId)] = command;
        return new ReactiveSagaStepFluent<TStep, TMessage>(this, Operation, CurrentContextMessage);
        Task Operation() => Send(command);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerType, SagaId);
    }

    public Task MarkAsComplete<TStep>() where TStep : IMessage
    {
        StepMessages.TryGetValue((typeof(TStep), CurrentContextMessage.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.LogStepAsync(SagaId, stepMessage.MessageId, CurrentContextMessage.MessageId, typeof(TStep),
            StepStatus.Completed, HandlerType, stepMessage);
    }


    public async Task MarkAsFailed<TStep>() where TStep : IMessage
    {
        StepMessages.TryGetValue((typeof(TStep), CurrentContextMessage.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        await SagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.ParentMessageId, typeof(TStep),
            StepStatus.Failed, HandlerType, stepMessage);
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep));
    }

    public async Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        StepMessages.TryGetValue((typeof(TStep), CurrentContextMessage.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        await SagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.ParentMessageId, typeof(TStep),
            StepStatus.Compensated, HandlerType, stepMessage);
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep), CurrentContextMessage);
    }

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage
    {
        StepMessages.TryGetValue((typeof(TStep), CurrentContextMessage.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.ParentMessageId, typeof(TStep),
            StepStatus.CompensationFailed, HandlerType, stepMessage);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        StepMessages.TryGetValue((typeof(T), CurrentContextMessage.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, typeof(T), HandlerType);
    }
}

public class SagaContext<TMessage, TSagaData>(
    Guid sagaId,
    TMessage currentContextMessage,
    Type handlerType,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator)
    : SagaContext<TMessage>(sagaId, currentContextMessage, handlerType, eventBus, sagaStore, sagaIdGenerator,
            compensationCoordinator),
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
        @event.SetParentMessageId(CurrentContextMessage.MessageId);
        StepMessages[(typeof(TStep), @event.MessageId)] = @event;
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, @event, HandlerType, Data, _eventBus,
                _sagaStore, StepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, @event);

        Task Operation() => Publish(@event); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep command)
        where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(CurrentContextMessage.MessageId);
        StepMessages[(typeof(TStep), command.MessageId)] = command;
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, command, HandlerType, Data, _eventBus,
                _sagaStore, StepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, command);

        Task Operation() => Send(command); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TStepAdapter, TSagaDataAdapter>(
    Guid sagaId,
    TStepAdapter adapter,
    Type handlerType,
    TSagaDataAdapter data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TStepAdapter, TSagaDataAdapter>
    where TStepAdapter : IMessage
    where TSagaDataAdapter : SagaData
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    public TStepAdapter Adapter { get; } = adapter; //Message adapter
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerType { get; } = handlerType;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand
    {
        stepMessages[(typeof(T), command.MessageId)] = command;
        return eventBus.Send(command, HandlerType, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerType, sagaId);
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerType, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue((typeof(TMarkStep), Adapter.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.MessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerType, stepMessage);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue((typeof(TMarkStep), Adapter.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.MessageId, typeof(TMarkStep),
            StepStatus.Failed, HandlerType, stepMessage);
    }

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue((typeof(TMarkStep), Adapter.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.MessageId, typeof(TMarkStep),
            StepStatus.Compensated, HandlerType, stepMessage);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue((typeof(TMarkStep), Adapter.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.LogStepAsync(SagaId, stepMessage.MessageId, stepMessage.MessageId, typeof(TMarkStep),
            StepStatus.CompensationFailed, HandlerType, stepMessage);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage
    {
        stepMessages.TryGetValue((typeof(TMarkStep), Adapter.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, typeof(TMarkStep), HandlerType);
    }

    /// <summary>
    /// Registers a step message by storing it in the step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage
    {
        stepMessages[(typeof(TMessage), message.MessageId)] = message;
    }

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep @event)
    {
        @event.SetSagaId(SagaId);
        @event.SetParentMessageId(Adapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), @event.MessageId)] = @event;
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, Adapter);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep command)
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(Adapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), command.MessageId)] = command;
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(this, Operation, Adapter);

        Task Operation() => Send(command); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep @event)
        where TNewStep : IEvent
    {
        @event.SetSagaId(SagaId);
        @event.SetParentMessageId(Adapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TNewStep), @event.MessageId)] = @event;
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, @event, HandlerType, Data, eventBus,
                SagaStore, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep command)
        where TNewStep : ICommand
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(Adapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TNewStep), command.MessageId)] = command;
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, command, HandlerType, Data, eventBus,
                SagaStore, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }
}