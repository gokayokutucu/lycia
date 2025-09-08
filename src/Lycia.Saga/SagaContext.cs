// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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
    public ISagaStore SagaStore { get; } = sagaStore;
    protected TInitialMessage CurrentStep { get; } = currentStep;
    public Guid SagaId { get; } = sagaId == Guid.Empty ? sagaIdGenerator.Generate() : sagaId;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;
    

    public Task Send<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, SagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId, cancellationToken);
    }

    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent,
        CancellationToken cancellationToken = default)
        where TNextStep : IEvent
    {
        var nextEventType = nextEvent.GetType();

        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);

        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TInitialMessage>.Create(
            CurrentStep.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Publish(nextEvent, null, cancellationToken);
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand,
        CancellationToken cancellationToken = default)
        where TNextStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);

        var adapterContext =
            new StepSpecificSagaContextAdapter<TInitialMessage>(SagaId, CurrentStep, HandlerTypeOfCurrentStep, eventBus,
                SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TInitialMessage>.Create(
            CurrentStep.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Send(nextCommand, cancellationToken);
    }

    public Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, cancellationToken);
    }

    public virtual Task MarkAsCompensated<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(sagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }

    public virtual Task CompensateAndBubbleUp<TStep>(CancellationToken cancellationToken = default)
        where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return Task.CompletedTask;
    }

    public virtual Task MarkAsComplete<TStep>() where TStep : IMessage
    {
        return SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }


    public virtual async Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>((Exception?)null, cancellationToken);
    }

    public virtual async Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default)
        where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, cancellationToken);
    }

    public virtual async Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default)
        where TStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail), cancellationToken);
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

    public virtual Task MarkAsCancelled<TStep>(Exception? ex) where TStep : IMessage
        => SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId,
            CurrentStep.GetType(), StepStatus.Cancelled, HandlerTypeOfCurrentStep,
            CurrentStep, ex);

    public Task<bool> IsAlreadyCompleted<T>() where T : IMessage
    {
        return SagaStore.IsStepCompletedAsync(SagaId, CurrentStep.MessageId, CurrentStep.GetType(),
            HandlerTypeOfCurrentStep);
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

    public new ISagaStepFluent PublishWithTracking<TStep>(TStep nextEvent,
        CancellationToken cancellationToken = default)
        where TStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);

        // Use the adapter, passing the service instances from this SagaContext<TMessage,TSagaData> instance
        var adapterContext =
            StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>.Create(
                CurrentStep.GetType(),
                Data.GetType(),
                SagaId, CurrentStep,
                HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator);

        return (ISagaStepFluent)CoordinatedSagaStepFluent<TInitialMessage, TSagaData>.Create(
            CurrentStep.GetType(),
            Data.GetType(),
            adapterContext,
            Operation);

        Task Operation() =>
            Publish(nextEvent, null,
                cancellationToken); // Explicitly call base to ensure it's the intended IEventBus.Publish
    }

    public new ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand, CancellationToken cancellationToken = default)
        where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);

        var adapterContext =
            StepSpecificSagaContextAdapter<TInitialMessage, TSagaData>.Create(
                CurrentStep.GetType(),
                Data.GetType(),
                SagaId, CurrentStep,
                HandlerTypeOfCurrentStep, Data, _eventBus,
                _sagaStore, _compensationCoordinator);

        return (ISagaStepFluent)CoordinatedSagaStepFluent<TInitialMessage, TSagaData>.Create(
            CurrentStep.GetType(),
            Data.GetType(),
            adapterContext,
            Operation);
        Task Operation() => Send(nextCommand, cancellationToken); // Explicitly call base
    }

    public override async Task MarkAsComplete<TStep>()
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (Exception?)null);
    }

    public override Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>((Exception?)null, cancellationToken);
    }

    public override Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, cancellationToken);
    }

    public override async Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default)
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = CurrentStep.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail), cancellationToken);
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

    public override async Task CompensateAndBubbleUp<TStep>(CancellationToken cancellationToken = default)
    {
        await _sagaStore.SaveSagaDataAsync(SagaId, Data);
        await _compensationCoordinator.CompensateParentAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep, cancellationToken);
    }

    public override Task MarkAsCancelled<TStep>(Exception? ex)
        => SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId,
            CurrentStep.GetType(), StepStatus.Cancelled, HandlerTypeOfCurrentStep, CurrentStep, ex);
}

// Internal adapter class as specified
internal class StepSpecificSagaContextAdapter<TCurrentStepAdapter>(
    Guid sagaId,
    TCurrentStepAdapter currentStep,
    Type handlerTypeOfCurrentStep,
    IEventBus eventBus,
    ISagaStore sagaStore,
    ISagaCompensationCoordinator compensationCoordinator)
    : ISagaContext<TCurrentStepAdapter>
    where TCurrentStepAdapter : IMessage
{
    public Guid SagaId => sagaId;
    private TCurrentStepAdapter CurrentStep { get; } = currentStep;
    public ISagaStore SagaStore => sagaStore;
    public Type HandlerTypeOfCurrentStep { get; } = handlerTypeOfCurrentStep;

    public Task Send<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        command.SetSagaId(SagaId);
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, handlerType, SagaId, cancellationToken);
    }

    /// <summary>
    /// Publishes a next event with saga-specific tracking information and returns a fluent interface for chaining saga operations.
    /// </summary>
    /// <typeparam name="TNextStep">The type of the next event in the saga flow.</typeparam>
    /// <param name="nextEvent">The event to publish, representing the next step of the saga.</param>
    /// <param name="cancellationToken">A cancellation token to observe while awaiting the operation.</param>
    /// <returns>An instance of <see cref="ISagaStepFluent"/> that allows fluent chaining of saga operations.</returns>
    public ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent,
        CancellationToken cancellationToken = default)
        where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(CurrentStep.MessageId);

        var nextStepContext =
            new StepSpecificSagaContextAdapter<TCurrentStepAdapter>(sagaId, CurrentStep, HandlerTypeOfCurrentStep,
                eventBus,
                SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Publish(nextEvent, null, cancellationToken);
    }

    /// <summary>
    /// Sends a command with tracking capabilities, ensuring that the provided command is associated with the current Saga instance
    /// by linking its Saga ID and parent message ID. The command is then registered in the internal step messages dictionary
    /// and processed within the context of the Saga infrastructure.
    /// </summary>
    /// <typeparam name="TStep">The type of the command to be sent and tracked.</typeparam>
    /// <param name="nextCommand">The command instance to be sent and tracked.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An instance of <see cref="ISagaStepFluent"/> for fluent continuation of the Saga workflow.</returns>
    public ISagaStepFluent SendWithTracking<TStep>(TStep nextCommand, CancellationToken cancellationToken = default)
        where TStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(CurrentStep.MessageId);

        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            sagaId, CurrentStep,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            CurrentStep.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand, cancellationToken);
    }

    public Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, cancellationToken);
    }

    public Task MarkAsComplete<TAdapterStep>() where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null);
    }

    public Task MarkAsFailed<TAdapterStep>(CancellationToken cancellationToken = default) where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>((Exception?)null, cancellationToken);
    }

    public Task MarkAsFailed<TAdapterStep>(Exception? ex, CancellationToken cancellationToken = default)
        where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return MarkAsFailed<TAdapterStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, cancellationToken);
    }

    public async Task MarkAsFailed<TAdapterStep>(FailResponse fail, CancellationToken cancellationToken = default)
        where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, CurrentStep.GetType(), HandlerTypeOfCurrentStep,
            CurrentStep, new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail),
            cancellationToken);
    }

    public Task MarkAsCompensated<TAdapterStep>() where TAdapterStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, CurrentStep, (SagaStepFailureInfo?)null);
    }

    public Task CompensateAndBubbleUp<TAdapterStep>(CancellationToken cancellationToken = default)
        where TAdapterStep : IMessage
    {
        // Step log should be in the compensation coordinator
        return Task.CompletedTask;
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

    public Task MarkAsCancelled<TAdapterStep>(Exception? ex) where TAdapterStep : IMessage
        => SagaStore.LogStepAsync(SagaId, CurrentStep.MessageId, CurrentStep.ParentMessageId, CurrentStep.GetType(),
            StepStatus.Cancelled, HandlerTypeOfCurrentStep, CurrentStep, ex);

    public Task<bool> IsAlreadyCompleted<TAdapterStep>() where TAdapterStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, CurrentStep.MessageId, CurrentStep.GetType(),
            HandlerTypeOfCurrentStep);
    }

    public static object Create(
        Type messageType,
        Guid sagaId,
        object currentStep,
        Type handlerType,
        IEventBus eventBus,
        ISagaStore sagaStore,
        ISagaCompensationCoordinator compensationCoordinator)
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
            compensationCoordinator
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
    ISagaCompensationCoordinator compensationCoordinator)
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

    public Task Send<T>(T command, CancellationToken cancellationToken = default) where T : ICommand
    {
        return eventBus.Send(command, HandlerTypeOfCurrentStep, sagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, sagaId, cancellationToken);
    }

    public Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent
    {
        return eventBus.Publish(@event, handlerType, sagaId, cancellationToken);
    }

    // Explicit interface implementation for ISagaContext<TStepAdapter>'s tracking methods
    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.PublishWithTracking<TReactiveStep>(
        TReactiveStep nextEvent, CancellationToken cancellationToken)
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property

        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Publish(nextEvent, null, cancellationToken); // Calls this adapter's Publish
    }

    ISagaStepFluent ISagaContext<TCurrentStepAdapter>.SendWithTracking<TReactiveStep>(
        TReactiveStep nextCommand, CancellationToken cancellationToken)
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId);

        var nextStepContext = StepSpecificSagaContextAdapter<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, eventBus,
            SagaStore, compensationCoordinator);

        return (ISagaStepFluent)ReactiveSagaStepFluent<TCurrentStepAdapter>.Create(
            StepAdapter.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand, cancellationToken); // Calls this adapter's Send
    }

    // 'new' methods for ISagaContext<TStepAdapter, TSagaDataAdapter>
    public ISagaStepFluent PublishWithTracking<TNextStep>(
        TNextStep nextEvent, CancellationToken cancellationToken = default)
        where TNextStep : IEvent
    {
        nextEvent.SetSagaId(SagaId);
        nextEvent.SetParentMessageId(StepAdapter.MessageId); // Assuming Adapter has a MessageId property
        // Create a new adapter for the next step, maintaining the original service instances and data type TSagaDataAdapter
        var nextStepContext = Create(
            StepAdapter.GetType(),
            Data.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, Data, eventBus,
            SagaStore, compensationCoordinator);

        return (ISagaStepFluent)CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>.Create(
            StepAdapter.GetType(),
            Data.GetType(),
            nextStepContext,
            Operation);
        Task Operation() => Publish(nextEvent, null, cancellationToken); // Calls this adapter's Publish
    }

    public ISagaStepFluent SendWithTracking<TNextStep>(
        TNextStep nextCommand, CancellationToken cancellationToken = default)
        where TNextStep : ICommand
    {
        nextCommand.SetSagaId(SagaId);
        nextCommand.SetParentMessageId(StepAdapter.MessageId);

        var nextStepContext = Create(
            StepAdapter.GetType(),
            Data.GetType(),
            sagaId, StepAdapter,
            HandlerTypeOfCurrentStep, Data, eventBus,
            SagaStore, compensationCoordinator);

        return (ISagaStepFluent)CoordinatedSagaStepFluent<TCurrentStepAdapter, TSagaDataAdapter>.Create(
            StepAdapter.GetType(),
            Data.GetType(),
            nextStepContext,
            Operation);

        Task Operation() => Send(nextCommand, cancellationToken); // Calls this adapter's Send
    }

    // Assuming FailedEventBase is accessible here or defined appropriately
    public Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : FailedEventBase
    {
        @event.SetSagaId(SagaId);
        return eventBus.Publish(@event, HandlerTypeOfCurrentStep, SagaId, cancellationToken);
    }

    public async Task MarkAsComplete<TMarkStep>()
        where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Completed, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null);
    }

    public Task MarkAsFailed<TMarkStep>(CancellationToken cancellationToken = default) where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>((Exception?)null, cancellationToken);
    }

    public Task MarkAsFailed<TMarkStep>(Exception? ex, CancellationToken cancellationToken = default)
        where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        return MarkAsFailed<TMarkStep>(new FailResponse
        {
            Reason = "Saga step failed",
            ExceptionType = ex?.GetType().Name,
            ExceptionDetail = ex?.ToString()
        }, cancellationToken);
    }

    public async Task MarkAsFailed<TMarkStep>(FailResponse fail, CancellationToken cancellationToken = default)
        where TMarkStep : IMessage
    {
        // Mark the step as failed in the saga data for triggering the HandleFailResponseAsync in CoordinatedSagaHandler
        Data.FailedStepType = StepAdapter.GetType();
        Data.FailedHandlerType = HandlerTypeOfCurrentStep;
        Data.FailedAt = DateTime.UtcNow;

        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep,
            StepAdapter,
            new SagaStepFailureInfo(fail.Reason, fail.ExceptionType, fail.ExceptionDetail), cancellationToken);
    }

    public async Task MarkAsCompensated<TMarkStep>()
        where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        await sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Compensated, HandlerTypeOfCurrentStep, StepAdapter, (Exception?)null);
    }

    public async Task CompensateAndBubbleUp<TMarkStep>(CancellationToken cancellationToken = default)
        where TMarkStep : IMessage
    {
        await sagaStore.SaveSagaDataAsync(SagaId, Data);
        // Step log should be in the compensation coordinator
        await compensationCoordinator.CompensateParentAsync(SagaId, StepAdapter.GetType(), HandlerTypeOfCurrentStep,
            StepAdapter, cancellationToken);
    }

    public Task MarkAsCompensationFailed<TMarkStep>()
        where TMarkStep : IMessage
    {
        return MarkAsCompensationFailed<TMarkStep>((Exception?)null);
    }

    public Task MarkAsCompensationFailed<TMarkStep>(Exception? ex)
        where TMarkStep : IMessage
    {
        return sagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.CompensationFailed, HandlerTypeOfCurrentStep, StepAdapter, ex);
    }

    public Task MarkAsCancelled<TStep>(Exception? ex)
        where TStep : IMessage
        => SagaStore.LogStepAsync(SagaId, StepAdapter.MessageId, StepAdapter.ParentMessageId, StepAdapter.GetType(),
            StepStatus.Cancelled, HandlerTypeOfCurrentStep, StepAdapter, ex);

    public Task<bool> IsAlreadyCompleted<TMarkStep>()
        where TMarkStep : IMessage
    {
        return sagaStore.IsStepCompletedAsync(SagaId, StepAdapter.MessageId, StepAdapter.GetType(),
            HandlerTypeOfCurrentStep);
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
        ISagaCompensationCoordinator compensationCoordinator)
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
            compensationCoordinator
        )!;
    }
}