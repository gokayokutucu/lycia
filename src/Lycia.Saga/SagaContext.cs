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

    public Task Send<T>(T command, CancellationToken ct = default) where T : ICommand
    {
        var commandType = command.GetType();
        StepMessages[(commandType, command.MessageId)] = command;
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, SagaId, ct);
    }

    public Task Publish<T>(T @event, CancellationToken ct = default) where T : IEvent
    {
        var eventType = @event.GetType();
        StepMessages[(eventType, @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, ct);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken ct = default) where T : IEvent
    {
        var eventType = @event.GetType();
        StepMessages[(eventType, @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId, ct);
    }

    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken ct = default)
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
        Task Operation() => Publish(nextEvent, null, ct);
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken ct = default)
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
        Task Operation() => Send(nextCommand, ct);
    }

    public Task Compensate<T>(T @event, CancellationToken ct = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        StepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, ct);
    }

    public virtual Task MarkAsCompensated<TStep>(CancellationToken ct = default) where TStep : IMessage
    {
        return SagaStore.LogStepAsync(sagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null, ct);
    }

    public virtual Task CompensateAndBubbleUp<TStep>(CancellationToken ct = default) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return Task.CompletedTask;
    }

    public virtual Task MarkAsComplete<TStep>(CancellationToken ct = default) where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null, ct);
    }


    public virtual async Task MarkAsFailed<TStep>(CancellationToken ct = default) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>((Exception?)null, ct);
    }

    public virtual async Task MarkAsFailed<TStep>(Exception? ex, CancellationToken ct = default) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, ct);
    }

    public virtual async Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken ct = default) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public virtual Task MarkAsCompensationFailed<TStep>(CancellationToken ct = default) where TStep : IMessage
    {
        return MarkAsCompensationFailed<TStep>(null, ct);
    }

    public virtual Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken ct = default) where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex, ct);
    }

    public Task<bool> IsAlreadyCompleted<T>(CancellationToken ct = default) where T : IMessage
    {
        StepMessages.TryGetValue((CurrentStep.GetType(), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return SagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, ct);
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

    public new ISagaStepFluent PublishWithTracking<TStep>(TStep nextEvent, CancellationToken ct = default)
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
            Publish(nextEvent, null, ct); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand, CancellationToken ct = default)
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
        Task Operation() => Send(nextCommand, ct); // Explicitly call base
    }

    public override async Task MarkAsComplete<TStep>(CancellationToken ct = default)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null, ct);
    }

    public override Task MarkAsFailed<TStep>(CancellationToken ct = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>((Exception?)null, ct);
    }

    public override Task MarkAsFailed<TStep>(Exception? ex, CancellationToken ct = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, ct);
    }

    public override async Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken ct = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = CurrentStep.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await _sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await _compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public override async Task MarkAsCompensated<TStep>(CancellationToken ct = default)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null, ct);
    }

    public override Task MarkAsCompensationFailed<TStep>(CancellationToken ct = default)
    {
        return MarkAsCompensationFailed<TStep>((Exception?)null, ct);
    }

    public override async Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken ct = default)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex, ct);
    }

    public override async Task CompensateAndBubbleUp<TStep>(CancellationToken ct = default)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
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

    public Task Send<T>(T command, CancellationToken ct = default) where T : ICommand
    {
        stepMessages[(command.GetType(), command.MessageId)] = command;
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId, ct);
    }

    public Task Publish<T>(T @event, CancellationToken ct = default) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId, ct);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken ct = default) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId, ct);
    }

    /// <summary>
    /// Publishes a next event with saga-specific tracking information and returns a fluent interface for chaining saga operations.
    /// </summary>
    /// <typeparam name="TNextStep">The type of the next event in the saga flow.</typeparam>
    /// <param name="nextEvent">The event to publish, representing the next step of the saga.</param>
    /// <param name="ct">A cancellation token to observe while awaiting the operation.</param>
    /// <returns>An instance of <see cref="ISagaStepFluent"/> that allows fluent chaining of saga operations.</returns>
    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken ct = default)
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

        Task Operation() => Publish(nextEvent, null, ct);
    }

    /// <summary>
    /// Sends a command with tracking capabilities, ensuring that the provided command is associated with the current Saga instance
    /// by linking its Saga ID and parent message ID. The command is then registered in the internal step messages dictionary
    /// and processed within the context of the Saga infrastructure.
    /// </summary>
    /// <typeparam name="TStep">The type of the command to be sent and tracked.</typeparam>
    /// <param name="nextCommand">The command instance to be sent and tracked.</param>
    /// <param name="ct">The token to monitor for cancellation requests.</param>
    /// <returns>An instance of <see cref="ISagaStepFluent"/> for fluent continuation of the Saga workflow.</returns>
    public ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand, CancellationToken ct = default)
        where TStep : ICommand
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

        Task Operation() => Send(nextCommand, ct);
    }

    public Task Compensate<T>(T @event, CancellationToken ct = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, ct);
    }

    public Task MarkAsComplete<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null, ct);
    }

    public Task MarkAsFailed<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>((Exception?)null, ct);
    }

    public Task MarkAsFailed<TAdapterStep>(Exception? ex, CancellationToken ct = default) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, ct);
    }

    public async Task MarkAsFailed<TAdapterStep>(FailResponse fail, CancellationToken ct = default) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep, new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public Task MarkAsCompensated<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null, ct);
    }

    public Task CompensateAndBubbleUp<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return Task.CompletedTask;
    }

    public Task MarkAsCompensationFailed<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        return MarkAsCompensationFailed<TAdapterStep>((Exception?)null, ct);
    }

    public Task MarkAsCompensationFailed<TAdapterStep>(Exception? ex, CancellationToken ct = default) where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, CurrentStep, ex, ct);
    }

    public Task<bool> IsAlreadyCompleted<TAdapterStep>(CancellationToken ct = default) where TAdapterStep : IMessage
    {
        stepMessages.TryGetValue((CurrentStep.GetType(), CurrentStep.MessageId), out var stepMessage);
        if (stepMessage == null) throw new InvalidOperationException();
        return sagaStore.IsStepCompletedAsync(SagaId, stepMessage.MessageId, CurrentStep.GetType(), HandlerTypeOfCurrentStep, ct);
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

    public Task Send<T>(T command, CancellationToken ct = default) where T : ICommand
    {
        stepMessages[(command.GetType(), command.MessageId)] = command;
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId, ct);
    }

    public Task Publish<T>(T @event, CancellationToken ct = default) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId, ct);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken ct = default) where T : IEvent
    {
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, handlerType, sagaId, ct);
    }

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep nextEvent, CancellationToken ct)
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

        Task Operation() => Publish(nextEvent, null, ct); // Calls this adapter's Publish
    }

    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep nextCommand, CancellationToken ct)
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

        Task Operation() => Send(nextCommand, ct); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public ISagaStepFluent PublishWithTracking<TNextStep>(
        TNextStep nextEvent, CancellationToken ct = default)
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
        Task Operation() => Publish(nextEvent, null, ct); // Calls this adapter's Publish
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(
        TNextStep nextCommand, CancellationToken ct = default)
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

        Task Operation() => Send(nextCommand, ct); // Calls this adapter's Send
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event, CancellationToken ct = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        // After setting the SagaId, store the event in the step messages
        stepMessages[(@event.GetType(), @event.MessageId)] = @event;
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, ct);
    }

    public async Task MarkAsComplete<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null, ct);
    }

    public Task MarkAsFailed<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>((Exception?)null, ct);
    }

    public Task MarkAsFailed<TMarkStep>(Exception? ex, CancellationToken ct = default) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, ct);
    }

    public async Task MarkAsFailed<TMarkStep>(FailResponse fail, CancellationToken ct = default) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = StepAdapter.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep, StepAdapter,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail));
    }

    public async Task MarkAsCompensated<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null, ct);
    }

    public async Task CompensateAndBubbleUp<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data, ct);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep,
            StepAdapter);
    }

    public Task MarkAsCompensationFailed<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        return MarkAsCompensationFailed<TMarkStep>((Exception?)null, ct);
    }

    public Task MarkAsCompensationFailed<TMarkStep>(Exception? ex, CancellationToken ct = default) where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, StepAdapter, ex, ct);
    }

    public Task<bool> IsAlreadyCompleted<TMarkStep>(CancellationToken ct = default) where TMarkStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, StepAdapter.MessageId, StepAdapter.GetType(),
            HandlerTypeOfCurrentStep, ct);
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