using System.Collections.Concurrent;
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Extensions;

namespace Lycia.Saga;

public class SagaContext<TInitialMessage>(
    Guid sagaId,
    TInitialMessage currentStep,
    Type handlerTypeOfCurrentStep,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator) : ISagaContext<TInitialMessage>
    where TInitialMessage : IMessage
{
    protected readonly ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> StepMessages = new();
    public ISagaStore SagaStore { get; } = sagaStore;
    protected TInitialMessage CurrentStep { get; } = currentStep;
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

    public ReactiveSagaStepFluent<TInitialMessage> PublishWithTracking<TStep>(TStep nextEvent) where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextEvent.MessageId)] = nextEvent;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TInitialMessage>(adapterContext, Operation);
        Task Operation() => Publish(nextEvent, null);
    }

    public ReactiveSagaStepFluent<TInitialMessage> SendWithTracking<TStep>(TStep nextCommand) where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);
        
        return new ReactiveSagaStepFluent<TInitialMessage>(adapterContext, Operation);
        Task Operation() => Send(nextCommand);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        StepMessages[(typeof(T), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(sagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TStep),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep);
    }

    public async Task CompensateAndBubbleUp<TStep>() where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, typeof(TStep),  HandlerTypeOfCurrentStep, CurrentStep);
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

public class SagaContext<TInitialMessage, TSagaData>(
    Guid sagaId,
    TInitialMessage currentStep,
    Type handlerTypeOfCurrentStep,
    TSagaData data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    ISagaCompensationCoordinator compensationCoordinator)
    : SagaContext<TInitialMessage>(sagaId, currentStep, handlerTypeOfCurrentStep, eventBus, sagaStore, sagaIdGenerator,
            compensationCoordinator),
        ISagaContext<TInitialMessage, TSagaData>
    where TSagaData : new()
    where TInitialMessage : IMessage
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ISagaStore _sagaStore = sagaStore;
    private readonly ISagaCompensationCoordinator _compensationCoordinator = compensationCoordinator;

    public TSagaData Data { get; } = data;

    public new CoordinatedSagaStepFluent<TInitialMessage, TSagaData> PublishWithTracking<TStep>(TStep nextEvent)
        where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextEvent.MessageId)] = nextEvent;
        
        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);
        return new CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(adapterContext, Operation);

        Task Operation() => Publish(nextEvent, null); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new CoordinatedSagaStepFluent<TInitialMessage, TSagaData> SendWithTracking<TStep>(TStep nextCommand)
        where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        
        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);
        return new CoordinatedSagaStepFluent<TInitialMessage, TSagaData>(adapterContext, Operation);

        Task Operation() => Send(nextCommand); // Explicitly call base
    }
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TCurrentStepAdapter>(
    Guid sagaId,
    TCurrentStepAdapter currentStep,
    Type handlerTypeOfCurrentStep,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaCompensationCoordinator compensationCoordinator,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TCurrentStepAdapter>
    where TCurrentStepAdapter : IMessage
{
    public Guid SagaId => sagaId;
    private TCurrentStepAdapter CurrentStep { get; } = currentStep;
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
    /// <typeparam name="TNextStep">The type of event to publish.</typeparam>
    /// <param name="nextEvent">The event instance to publish.</param>
    /// <returns>A ReactiveSagaStepFluent for the published step.</returns>
    public ReactiveSagaStepFluent<TCurrentStepAdapter> PublishWithTracking<TNextStep>(TNextStep nextEvent) where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(typeof(TCurrentStepAdapter), nextEvent.MessageId)] = nextEvent;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TCurrentStepAdapter>(nextStepContext, Operation);

        Task Operation() => Publish(nextEvent, null);
    }

    /// <summary>
    /// Sends a command with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of command to send.</typeparam>
    /// <param name="nextCommand">The command instance to send.</param>
    /// <returns>A ReactiveSagaStepFluent for the sent step.</returns>
    public ReactiveSagaStepFluent<TCurrentStepAdapter> SendWithTracking<TStep>(TStep nextCommand) where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(typeof(TStep), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TCurrentStepAdapter>(nextStepContext, Operation);

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

    public Task MarkAsCompensated<TStep1>() where TStep1 : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, typeof(TStep1),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep);
    }

    public async Task CompensateAndBubbleUp<TStep1>() where TStep1 : IMessage
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

internal class StepSpecificSagaContextAdapter<TCurrentStepAdapter, TSagaDataAdapter>(
    Guid sagaId,
    TCurrentStepAdapter stepAdapter,
    Type handlerTypeOfCurrentStep,
    TSagaDataAdapter data,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaCompensationCoordinator compensationCoordinator,
    ConcurrentDictionary<(Type StepType, Guid MessageId), IMessage> stepMessages)
    : ISagaContext<TCurrentStepAdapter, TSagaDataAdapter>
    where TCurrentStepAdapter : IMessage
    where TSagaDataAdapter : new()
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    private TCurrentStepAdapter StepAdapter { get; } = stepAdapter; //Message adapter
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
    
    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ReactiveSagaStepFluent<TCurrentStepAdapter> ISagaContext<TCurrentStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep nextEvent)
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), nextEvent.MessageId)] = nextEvent;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TCurrentStepAdapter>(nextStepContext, Operation);

        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    ReactiveSagaStepFluent<TCurrentStepAdapter> ISagaContext<TCurrentStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep nextCommand)
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TReactiveStep), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        
        return new ReactiveSagaStepFluent<TCurrentStepAdapter>(nextStepContext, Operation);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter> PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TCurrentStepAdapter), nextEvent.MessageId)] = nextEvent;
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter, TSagaDataAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, Data, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        return new CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>(nextStepContext, Operation);

        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    public CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter> SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(typeof(TNextStep), nextCommand.MessageId)] = nextCommand;
        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter, TSagaDataAdapter>(sagaId, StepAdapter, HandlerTypeOfCurrentStep, Data, eventBus,
                SagaStore, compensationCoordinator, stepMessages);
        return new CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>(nextStepContext, Operation);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
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
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, typeof(TMarkStep),
            StepStatus.Completed, HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        // Step log should be in the compensation coordinator
         return compensationCoordinator.CompensateAsync(SagaId, typeof(TMarkStep), HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, typeof(TMarkStep),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task CompensateAndBubbleUp<TMarkStep>() where TMarkStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return compensationCoordinator.CompensateParentAsync(SagaId, typeof(TMarkStep), HandlerTypeOfCurrentStep, StepAdapter);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, typeof(TMarkStep),
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
}