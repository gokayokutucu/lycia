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
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TMessage>(SagaId, CurrentContextMessage, HandlerType, eventBus,
                sagaStore, sagaIdGenerator, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(adapterContext, Operation, CurrentContextMessage, @event);
        Task Operation() => Publish(@event);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(CurrentContextMessage.MessageId);
        StepMessages[(typeof(TStep), command.MessageId)] = command;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TMessage>(SagaId, CurrentContextMessage, HandlerType, eventBus,
                sagaStore, sagaIdGenerator, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(adapterContext, Operation, CurrentContextMessage, command);
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
        return SagaStore.LogStepAsync(SagaId, CurrentContextMessage.MessageId, CurrentContextMessage.ParentMessageId, typeof(TStep),
            StepStatus.Completed, HandlerType, CurrentContextMessage);
    }


    public async Task MarkAsFailed<TStep>() where TStep : IMessage
    {
        await SagaStore.LogStepAsync(SagaId, CurrentContextMessage.MessageId, CurrentContextMessage.ParentMessageId, typeof(TStep),
            StepStatus.Failed, HandlerType, CurrentContextMessage);
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep));
    }

    public async Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        await SagaStore.LogStepAsync(SagaId, CurrentContextMessage.MessageId, CurrentContextMessage.ParentMessageId, typeof(TStep),
            StepStatus.Compensated, HandlerType, CurrentContextMessage);
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep), CurrentContextMessage);
    }

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentContextMessage.MessageId, CurrentContextMessage.ParentMessageId, typeof(TStep),
            StepStatus.CompensationFailed, HandlerType, CurrentContextMessage);
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
                _sagaStore, sagaIdGenerator, compensationCoordinator, StepMessages);
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
                _sagaStore, sagaIdGenerator, compensationCoordinator, StepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, command);

        Task Operation() => Send(command); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TMessage>(
    Guid sagaId,
    TMessage step,
    Type handlerType,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TMessage>
    where TMessage : IMessage
{
    public Guid SagaId => sagaId;
    public TMessage CurrentContextMessage { get; } = step;
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerType { get; } = handlerType;

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

    /// <summary>
    /// Publishes an event with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of event to publish.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <returns>A ReactiveSagaStepFluent for the published step.</returns>
    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep @event) where TStep : IEvent
    {
        @event.SetSagaId(SagaId);
        @event.SetParentMessageId(step.MessageId);
        stepMessages[(typeof(TMessage), @event.MessageId)] = @event;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TMessage>(sagaId, CurrentContextMessage, HandlerType, eventBus,
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(nextStepContext, Operation, CurrentContextMessage, @event);

        Task Operation() => Publish(@event);
    }

    /// <summary>
    /// Sends a command with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of command to send.</typeparam>
    /// <param name="command">The command instance to send.</param>
    /// <returns>A ReactiveSagaStepFluent for the sent step.</returns>
    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep command) where TStep : ICommand
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(step.MessageId);
        stepMessages[(typeof(TStep), command.MessageId)] = command;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TMessage>(sagaId, CurrentContextMessage, HandlerType, eventBus,
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(nextStepContext, Operation, CurrentContextMessage, command);

        Task Operation() => Send(command);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerType, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, step.MessageId, step.ParentMessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerType, step);
    }

    public async Task MarkAsFailed<TStep1>() where TStep1 : IMessage
    {
        await sagaStore.LogStepAsync(SagaId, step.MessageId, step.ParentMessageId, typeof(TStep1),
            StepStatus.Failed, HandlerType, step);
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep1));
    }

    public async Task MarkAsCompensated<TStep1>() where TStep1 : IMessage
    {
        await sagaStore.LogStepAsync(SagaId, step.MessageId, step.ParentMessageId, typeof(TStep1),
            StepStatus.Compensated, HandlerType, step);
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep1), step);
    }

    public Task MarkAsCompensationFailed<TStep1>() where TStep1 : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, step.MessageId, step.ParentMessageId, typeof(TStep1),
            StepStatus.CompensationFailed, HandlerType, step);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        stepMessages.TryGetValue((typeof(T), step.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, typeof(T), HandlerType);
    }

    /// <summary>
    /// Registers a step message by storing it in the step messages dictionary keyed by its type and message id.
    /// </summary>
    public void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage
    {
        stepMessages[(typeof(TMessage), message.MessageId)] = message;
    }
}

internal class StepSpecificSagaContextAdapter<TStepAdapter, TSagaDataAdapter>(
    Guid sagaId,
    TStepAdapter adapter,
    Type handlerType,
    TSagaDataAdapter data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator,
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
        return sagaStore.LogStepAsync(SagaId, Adapter.MessageId, Adapter.MessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerType, Adapter);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, Adapter.MessageId, Adapter.MessageId, typeof(TMarkStep),
            StepStatus.Failed, HandlerType, Adapter);
    }

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, Adapter.MessageId, Adapter.MessageId, typeof(TMarkStep),
            StepStatus.Compensated, HandlerType, Adapter);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, Adapter.MessageId, Adapter.MessageId, typeof(TMarkStep),
            StepStatus.CompensationFailed, HandlerType, Adapter);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, Adapter.MessageId, typeof(TMarkStep), HandlerType);
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
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TStepAdapter>(sagaId, Adapter, HandlerType, eventBus,
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(nextStepContext, Operation, Adapter, @event);

        Task Operation() => Publish(@event); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep command)
    {
        command.SetSagaId(SagaId);
        command.SetParentMessageId(Adapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), command.MessageId)] = command;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TStepAdapter>(sagaId, Adapter, HandlerType, eventBus,
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(nextStepContext, Operation, Adapter, command);

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
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
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
                SagaStore, sagaIdGenerator, compensationCoordinator, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, command);

        Task Operation() => Send(command); // Calls this adapter's Send
    }
}