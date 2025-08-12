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
        var messageStepType = message.GetType();
        StepMessages[(messageStepType, message.MessageId)] = message;
    }

    public Task Send<T>(T command) where T : ICommand
    {
        var commandType = command.GetType();
        StepMessages[(commandType, command.MessageId)] = command;
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        var eventType = @event.GetType();
        StepMessages[(eventType, @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        var eventType = @event.GetType();
        StepMessages[(eventType, @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId);
    }

    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent
    {
        var nextEventType = nextEvent.GetType();

        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(nextEventType, nextEvent.MessageId)] = nextEvent;

        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TInitialMessage>.Create(
            CurrentStep.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Publish(nextEvent, null);
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(nextCommand.GetType(), nextCommand.MessageId)] = nextCommand;

        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator, StepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TInitialMessage>.Create(
            CurrentStep.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Send(nextCommand);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        StepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public virtual Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(sagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }

    public virtual async Task CompensateAndBubbleUp<TStep>() where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep);
    }

    public virtual Task MarkAsComplete<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }


    public virtual async Task MarkAsFailed<TStep>() where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>((Exception?)null);
    }

    public virtual async Task MarkAsFailed<TStep>(Exception? ex) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        });
    }

    public virtual async Task MarkAsFailed<TStep>(FailResponse fail) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public virtual Task MarkAsCompensationFailed<TStep>() where TStep : IMessage
    {
        return MarkAsCompensationFailed<TStep>(null);
    }

    public virtual Task MarkAsCompensationFailed<TStep>(Exception? ex) where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex);
    }

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        StepMessages.TryGetValue((CurrentStep.GetType(), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, CurrentStep.GetType(), HandlerTypeOfCurrentStep);
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
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    private readonly IEventBus _eventBus = eventBus;
    private readonly ISagaStore _sagaStore = sagaStore;
    private readonly ISagaCompensationCoordinator _compensationCoordinator = compensationCoordinator;

    public TSagaData Data { get; } = data;

    public new ISagaStepFluent PublishWithTracking<TStep>(TStep nextEvent)
        where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(nextEvent.GetType(), nextEvent.MessageId)] = nextEvent;

        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>.Create(
                CurrentStep.GetType(),
                Data.GetType(),
                SagaId, CurrentStep,
                HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);

        return (ISagaStepFluent)CoordinatedSagaStepFluent<TInitialMessage, TSagaData>.Create(
            CurrentStep.GetType(),
            Data.GetType(),
            adapterContext,
            Operation);

        Task Operation() =>
            Publish(nextEvent, null); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand)
        where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        StepMessages[(nextCommand.GetType(), nextCommand.MessageId)] = nextCommand;

        var adapterContext =
            StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>.Create(
                CurrentStep.GetType(),
                Data.GetType(),
                SagaId, CurrentStep,
                HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator, StepMessages);
        
        return (ISagaStepFluent)CoordinatedSagaStepFluent<TInitialMessage, TSagaData>.Create(
            CurrentStep.GetType(),
            Data.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Send(nextCommand); // Explicitly call base
    }

    public override async Task MarkAsComplete<TStep>()
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }

    public override Task MarkAsFailed<TStep>()
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>((Exception?)null);
    }

    public override Task MarkAsFailed<TStep>(Exception? ex)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        });
    }

    public override async Task MarkAsFailed<TStep>(FailResponse fail)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = CurrentStep.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public override async Task MarkAsCompensated<TStep>()
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }

    public override Task MarkAsCompensationFailed<TStep>()
    {
        return MarkAsCompensationFailed<TStep>((Exception?)null);
    }

    public override async Task MarkAsCompensationFailed<TStep>(Exception? ex)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex);
    }

    public override async Task CompensateAndBubbleUp<TStep>()
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _compensationCoordinator.CompensateParentAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep);
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
        stepMessages[(command.GetType(), command.MessageId)] = command;
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId);
    }

    /// <summary>
    /// Publishes an event with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TNextStep">The type of event to publish.</typeparam>
    /// <param name="nextEvent">The event instance to publish.</param>
    /// <returns>A ReactiveSagaStepFluent for the published step.</returns>
    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(CurrentStep.GetType(), nextEvent.MessageId)] = nextEvent;

        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, CurrentStep, HandlerTypeOfCurrentStep,
                eventBus,
                SagaStore, compensationCoordinator, stepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Publish(nextEvent, null);
    }

    /// <summary>
    /// Sends a command with tracking for use in a reactive fluent chain, using this adapter as the ISagaContext for the new step.
    /// </summary>
    /// <typeparam name="TStep">The type of command to send.</typeparam>
    /// <param name="nextCommand">The command instance to send.</param>
    /// <returns>A ReactiveSagaStepFluent for the sent step.</returns>
    public ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand) where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);
        stepMessages[(nextCommand.GetType(), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            sagaId, CurrentStep,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator, stepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand);
    }

    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public Task MarkAsComplete<TAdapterStep>() where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null);
    }

    public Task MarkAsFailed<TAdapterStep>() where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>((Exception?)null);
    }

    public Task MarkAsFailed<TAdapterStep>(Exception? ex) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        });
    }

    public async Task MarkAsFailed<TAdapterStep>(FailResponse fail) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep, new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public Task MarkAsCompensated<TAdapterStep>() where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null);
    }

    public async Task CompensateAndBubbleUp<TAdapterStep>() where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep);
    }

    public Task MarkAsCompensationFailed<TAdapterStep>() where TAdapterStep : IMessage
    {
        return MarkAsCompensationFailed<TAdapterStep>((Exception?)null);
    }

    public Task MarkAsCompensationFailed<TAdapterStep>(Exception? ex) where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex);
    }

    public Task<bool> IsAlreadyCompleted<TAdapterStep>() where TAdapterStep : IMessage
    {
        stepMessages.TryGetValue((CurrentStep.GetType(), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, CurrentStep.GetType(), HandlerTypeOfCurrentStep);
    }

    /// <summary>
    /// Registers a step message by storing it in the step messages dictionary keyed by its type and message id.
    /// </summary>
    public void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage
    {
        stepMessages[(message.GetType(), message.MessageId)] = message;
    }
    
    public static object Create(
        Type messageType,
        Guid sagaId,
        object currentStep,
        Type handlerType,
        IEventBus eventBus,
        ISagaStore sagaStore,
        ISagaCompensationCoordinator compensationCoordinator,
        ConcurrentDictionary<(Type, Guid), IMessage> stepMessages)
    {
        var adapterOpen = typeof(StepSpecificSagaContextAdapter<>);
        var adapterClosed = adapterOpen.MakeGenericType(messageType);

        return Activator.CreateInstance(
            adapterClosed,
            sagaId,
            currentStep,
            handlerType,
            eventBus,
            sagaStore,
            compensationCoordinator,
            stepMessages
        )!;
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
    where TSagaDataAdapter : SagaData
{
    // Not used by ISagaContext methods but part of constructor signature

    public Guid SagaId => sagaId;
    private TCurrentStepAdapter StepAdapter { get; } = stepAdapter; //Message adapter
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;
    public TSagaDataAdapter Data => data;

    public Task Send<T>(T command) where T : ICommand
    {
        stepMessages[(command.GetType(), command.MessageId)] = command;
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId);
    }

    public Task Publish<T>(T @event, Type? handlerType) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, handlerType, sagaId);
    }

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep nextEvent)
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(nextEvent.GetType(), nextEvent.MessageId)] = nextEvent;

        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator, stepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep nextCommand)
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(nextCommand.GetType(), nextCommand.MessageId)] = nextCommand;

        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator, stepMessages);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public ISagaStepFluent PublishWithTracking<TNextStep>(
        TNextStep nextEvent)
        where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        stepMessages[(nextEvent.GetType(), nextEvent.MessageId)] = nextEvent;
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext = Create(
            StepAdapter.GetType(),
            Data.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, Data, eventBus,
            SagaStore, compensationCoordinator, stepMessages);
        
        return (ISagaStepFluent)CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>.Create(
            StepAdapter.GetType(),
            Data.GetType(),
            nextStepContext,
            Operation);
        Task Operation() => Publish(nextEvent, null); // Calls this adapter's Publish
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(
        TNextStep nextCommand)
        where TNextStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId prop erty
        stepMessages[(nextCommand.GetType(), nextCommand.MessageId)] = nextCommand;
        
        var nextStepContext = Create(
            StepAdapter.GetType(),
            Data.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, Data, eventBus,
            SagaStore, compensationCoordinator, stepMessages);
        
        return (ISagaStepFluent)CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>.Create(
            StepAdapter.GetType(),
            Data.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand); // Calls this adapter's Send
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId);
    }

    public async Task MarkAsComplete<TMarkStep>() where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null);
    }

    public Task MarkAsFailed<TMarkStep>() where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>((Exception?)null);
    }

    public Task MarkAsFailed<TMarkStep>(Exception? ex) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        });
    }

    public async Task MarkAsFailed<TMarkStep>(FailResponse fail) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = StepAdapter.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep, StepAdapter,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public async Task MarkAsCompensated<TMarkStep>() where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null);
    }

    public async Task CompensateAndBubbleUp<TMarkStep>() where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep,
            StepAdapter);
    }

    public Task MarkAsCompensationFailed<TMarkStep>() where TMarkStep : IMessage
    {
        return MarkAsCompensationFailed<TMarkStep>((Exception?)null);
    }

    public Task MarkAsCompensationFailed<TMarkStep>(Exception? ex) where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, StepAdapter, ex);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>() where TMarkStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, StepAdapter.MessageId, StepAdapter.GetType(),
            HandlerTypeOfCurrentStep);
    }

    /// <summary>
    /// Registers a step message by storing it in the step messages dictionary keyed by its type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the step message.</typeparam>
    /// <param name="message">The step message instance to register.</param>
    public void RegisterStepMessage<TMessage>(TMessage message) where TMessage : IMessage
    {
        stepMessages[(message.GetType(), message.MessageId)] = message;
    }

    public static object Create(
        Type messageType,
        Type sagaDataType,
        Guid sagaId,
        object currentStep,
        Type handlerType,
        object data,
        IEventBus eventBus,
        ISagaStore sagaStore,
        ISagaCompensationCoordinator compensationCoordinator,
        ConcurrentDictionary<(Type, Guid), IMessage> stepMessages)
    {
        var adapterOpen = typeof(StepSpecificSagaContextAdapter<,>);
        var adapterClosed = adapterOpen.MakeGenericType(messageType, sagaDataType);

        return Activator.CreateInstance(
            adapterClosed,
            sagaId,
            currentStep,
            handlerType,
            data,
            eventBus,
            sagaStore,
            compensationCoordinator,
            stepMessages
        )!;
    }
}