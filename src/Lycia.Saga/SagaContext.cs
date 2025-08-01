using System.Collections.Concurrent;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga;

public class SagaContext<TMessage>(
    Guid sagaId,
    TMessage currentStep,
    Type handlerTypeOfCurrentStep,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator) : ISagaContext<TMessage>
    where TMessage : IMessage
{
    protected readonly ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> StepMessages = new();
    public ISagaStore SagaStore { get; } = sagaStore;
    protected TMessage CurrentStep { get; } = currentStep;
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;
    
    /// <summary>
    /// Registers a step message by storing it in the internal step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessageStep">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessageStep>(TMessageStep message) where TMessageStep : IMessage
    {
        StepMessages[(typeof(TMessageStep), message.MessageId)] = message;
    }

    public Task Send<T>(T command) where T : ICommand
    {
        StepMessages[(typeof(T), command.MessageId)] = command;
        return eventBus.Send(command, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }
    
    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, handlerType, SagaId);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep nextEvent) where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextEvent.MessageId)] = nextEvent;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(adapterContext, Operation, CurrentStep, nextEvent);
        Task Operation() => Publish(nextEvent, null);
    }

    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep nextCommand) where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(adapterContext, Operation, CurrentStep, nextCommand);
        Task Operation() => Send(nextCommand);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task MarkAsComplete<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TStep),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep);
    }


    public async Task MarkAsFailed<TStep>() where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep), HandlerTypeOfCurrentStep, CurrentStep);
    }

    public async Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep),  HandlerTypeOfCurrentStep, CurrentStep);
    }

    public Task MarkAsCompensationFailed<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TStep),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        StepMessages.TryGetValue((typeof(T), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, typeof(T), HandlerTypeOfCurrentStep);
    }
}

public class SagaContext<TMessage, TSagaData>(
    Guid sagaId,
    TMessage currentStep,
    Type handlerTypeOfCurrentStep,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator)
    : SagaContext<TMessage>(sagaId, currentStep, handlerTypeOfCurrentStep, eventBus, sagaStore, sagaIdGenerator,
            compensationCoordinator),
        ISagaContext<TMessage, TSagaData>
    where TSagaData : new()
    where TMessage : IMessage
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ISagaStore _sagaStore = sagaStore;
    private readonly ISagaCompensationCoordinator _compensationCoordinator = compensationCoordinator;

    public TSagaData Data { get; } = data;

    public new CoordinatedSagaStepFluent<TStep, TSagaData> PublishWithTracking<TStep>(TStep nextEvent)
        where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextEvent.MessageId)] = nextEvent;
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, nextEvent, HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, nextEvent);

        Task Operation() => Publish(nextEvent, null); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TStep, TSagaData> SendWithTracking<TStep>(TStep nextCommand)
        where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        var adapterContext =
            new StepSpecificSagaContextAdapter<TStep, TSagaData>(SagaId, nextCommand, HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);
        return new CoordinatedSagaStepFluent<TStep, TSagaData>(adapterContext, Operation, nextCommand);

        Task Operation() => Send(nextCommand); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TMessage>(
    Guid sagaId,
    TMessage currentStep,
    Type handlerTypeOfCurrentStep,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaCompensationCoordinator compensationCoordinator,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TMessage>
    where TMessage : IMessage
{
    public Guid SagaId => sagaId;
    private TMessage CurrentStep { get; } = currentStep;
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;

    public Task Send<T>(T command) where T : ICommand
    {
        stepMessages[(typeof(T), command.MessageId)] = command;
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId);
    }
    
    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, handlerType, SagaId);
    }

    /// <summary>
    /// Publishes an event with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of event to publish.</typeparam>
    /// <param name="nextEvent">The event instance to publish.</param>
    /// <returns>A ReactiveSagaStepFluent for the published step.</returns>
    public ReactiveSagaStepFluent<TStep, TMessage> PublishWithTracking<TStep>(TStep nextEvent) where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(typeof(TMessage), nextEvent.MessageId)] = nextEvent;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TMessage>(sagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(nextStepContext, Operation, CurrentStep, nextEvent);

        Task Operation() => Publish(nextEvent, null);
    }

    /// <summary>
    /// Sends a command with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of command to send.</typeparam>
    /// <param name="nextCommand">The command instance to send.</param>
    /// <returns>A ReactiveSagaStepFluent for the sent step.</returns>
    public ReactiveSagaStepFluent<TStep, TMessage> SendWithTracking<TStep>(TStep nextCommand) where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TMessage>(sagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TStep, TMessage>(nextStepContext, Operation, CurrentStep, nextCommand);

        Task Operation() => Send(nextCommand);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep);
    }

    public async Task MarkAsFailed<TStep1>() where TStep1 : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, typeof(TStep1), HandlerTypeOfCurrentStep, CurrentStep);
    }

    public async Task MarkAsCompensated<TStep1>() where TStep1 : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep1), HandlerTypeOfCurrentStep, CurrentStep);
    }

    public Task MarkAsCompensationFailed<TStep1>() where TStep1 : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TStep1),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        stepMessages.TryGetValue((typeof(T), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, typeof(T), HandlerTypeOfCurrentStep);
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
    TStepAdapter stepAdapter,
    Type handlerTypeOfCurrentStep,
    TSagaDataAdapter data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaCompensationCoordinator compensationCoordinator,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TStepAdapter, TSagaDataAdapter>
    where TStepAdapter : IMessage
    where TSagaDataAdapter : new()
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    private TStepAdapter StepAdapter { get; } = stepAdapter; //Message adapter
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand
    {
        stepMessages[(typeof(T), command.MessageId)] = command;
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId);
    }
    
    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, handlerType, sagaId);
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        stepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.MessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        // Step log should be in the compensation coordinator
         return compensationCoordinator.CompensateAsync(SagaId, typeof(TMarkStep), HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return compensationCoordinator.CompensateParentAsync(SagaId, typeof(TMarkStep), HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.MessageId, typeof(TMarkStep),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, StepAdapter.MessageId, typeof(TMarkStep), HandlerTypeOfCurrentStep);
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
        TReactiveStep nextEvent)
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), nextEvent.MessageId)] = nextEvent;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TStepAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(nextStepContext, Operation, StepAdapter, nextEvent);

        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TReactiveStep, TStepAdapter> ISagaContext<TStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep nextCommand)
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TStepAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TReactiveStep, TStepAdapter>(nextStepContext, Operation, StepAdapter, nextCommand);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> PublishWithTracking<TNewStep>(TNewStep nextEvent)
        where TNewStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TNewStep), nextEvent.MessageId)] = nextEvent;
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, nextEvent, HandlerTypeOfCurrentStep, Data, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, nextEvent);

        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter> SendWithTracking<TNewStep>(TNewStep nextCommand)
        where TNewStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TNewStep), nextCommand.MessageId)] = nextCommand;
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TNewStep, TSagaDataAdapter>(sagaId, nextCommand, HandlerTypeOfCurrentStep, Data, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        return new CoordinatedSagaStepFluent<TNewStep, TSagaDataAdapter>(nextStepContext, Operation, nextCommand);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
    }
}