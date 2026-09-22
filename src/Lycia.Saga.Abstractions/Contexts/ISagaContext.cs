// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Contexts;

public interface ISagaContext
{
    Guid SagaId { get; }
    Type HandlerTypeOfCurrentStep { get; }
    ISagaStore SagaStore { get; }
}

public interface ISagaContext<TInitialMessage> : ISagaContext
    where TInitialMessage : IMessage
{
    Task Send<T>(T command, CancellationToken cancellationToken = default) where T : ICommand;
    Task Respond<TRequest, TResponse>(TRequest request, TResponse response, CancellationToken cancellationToken = default)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>;
    Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent;

    Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent;

    /// <summary>
    /// Creates a deferred tracked publish operation. The event is not published until a terminal method on
    /// the returned <see cref="ISagaStepFluent"/> (for example <c>ThenMarkAsComplete</c>) is awaited; that
    /// terminal method's <see cref="CancellationToken"/> is the single token governing both the deferred
    /// publish and the saga-step transition that follows it. This entry point never accepts a token itself.
    /// </summary>
    ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent;

    /// <summary>
    /// Creates a deferred tracked send operation. The command is not sent until a terminal method on the
    /// returned <see cref="ISagaStepFluent"/> (for example <c>ThenMarkAsComplete</c>) is awaited; that
    /// terminal method's <see cref="CancellationToken"/> is the single token governing both the deferred
    /// send and the saga-step transition that follows it. This entry point never accepts a token itself.
    /// </summary>
    ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand;

    /// <summary>
    /// Creates a deferred tracked respond operation. The response is not sent until a terminal method on
    /// the returned <see cref="ISagaStepFluent"/> (for example <c>ThenMarkAsComplete</c>) is awaited; that
    /// terminal method's <see cref="CancellationToken"/> is the single token governing both the deferred
    /// respond and the saga-step transition that follows it. This entry point never accepts a token itself.
    /// </summary>
    ISagaStepFluent RespondWithTracking<TRequest, TResponse>(TRequest request, TResponse response)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>;

    Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : IFailedEventBase;

    Task MarkAsComplete<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default) where TStep : IMessage;

    /// <summary>
    /// Marks the current saga step compensated and stops there - it does not propagate to the logical
    /// parent. Use this for a root/final compensation step, or use
    /// <see cref="ContinueCompensation"/><c>().ThenMarkAsCompensated&lt;TStep&gt;()</c> for the equivalent
    /// staged-fluent form; both call the same underlying transition.
    /// </summary>
    Task MarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    /// <summary>
    /// Marks the current saga step compensated and durably requires - then immediately attempts -
    /// compensation propagation to the logical parent (via <c>ParentMessageId</c>). This is the primitive
    /// behind <see cref="ContinueCompensation"/><c>().ThenMarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(cancellationToken)</c>;
    /// prefer that staged fluent form in application code. A step with no logical parent (a root step)
    /// only marks itself compensated; no propagation requirement is created. Once the propagation
    /// requirement is durably recorded, a crash or cancellation during the immediate attempt never erases
    /// it - a <c>CompensationWorker</c> recovers it. See <c>DEVELOPERS.md</c>, "Coordinated compensation
    /// continuation", for the durable propagation model.
    /// </summary>
    Task BubbleUpCompensationAsync<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    Task MarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCancelled<TStep>(Exception? ex = null, CancellationToken cancellationToken = default) where TStep : IMessage;

    /// <summary>
    /// Begins a coordinated compensation continuation for the current saga step. This does not perform any
    /// business rollback and does not execute anything by itself - it only returns a staged fluent object.
    /// Call <c>ThenMarkAsCompensated&lt;TStep&gt;(cancellationToken)</c> to mark the step compensated and
    /// stop there, or the no-token <c>ThenMarkAsCompensated&lt;TStep&gt;()</c> followed by
    /// <c>ThenBubbleUp(cancellationToken)</c> to also continue compensation through the logical parent
    /// lineage. This entry point never accepts a token itself - only the terminal method in the chain does.
    /// </summary>
    ICompensationContinuation ContinueCompensation();

    Task<bool> IsAlreadyCompleted<T>() where T : IMessage;
}

public interface ISagaContext<TInitialMessage, out TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    /// <inheritdoc cref="ISagaContext{TInitialMessage}.PublishWithTracking{TNextStep}"/>
    new ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent)
        where TNextStep : IEvent;

    /// <inheritdoc cref="ISagaContext{TInitialMessage}.SendWithTracking{TNextStep}"/>
    new ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand)
        where TNextStep : ICommand;

    /// <inheritdoc cref="ISagaContext{TInitialMessage}.RespondWithTracking{TRequest,TResponse}"/>
    new ISagaStepFluent RespondWithTracking<TRequest, TResponse>(TRequest request, TResponse response)
        where TRequest : IMessage
        where TResponse : IResponse<TRequest>;
}
