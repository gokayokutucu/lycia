// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Enums;
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

    Task MarkAsComplete<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default) where TStep : IMessage;

    /// <summary>
    /// Marks the current saga step compensated and stops there - it does not propagate to the logical
    /// parent. Use this for a root/final compensation step. Equivalent to the staged
    /// <see cref="MarkAsCompensated{TStep}()"/><c>().ThenBubbleUp(...)</c> form's first half, but terminal:
    /// both call the same underlying transition.
    /// </summary>
    Task MarkAsCompensated<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;

    /// <summary>
    /// Begins a coordinated compensation continuation for the current saga step. This does not perform any
    /// business rollback and does not execute anything by itself - it only returns a staged fluent object
    /// exposing <c>ThenBubbleUp(cancellationToken)</c>, which marks the step compensated and continues
    /// compensation through the logical parent lineage (via <c>ParentMessageId</c>) as one atomic
    /// operation. This entry point never accepts a token itself - only <c>ThenBubbleUp</c> does, and that
    /// single token governs the whole composite operation. There is no equivalent method directly on
    /// <c>ISagaContext</c> for the propagation itself - <c>ThenBubbleUp</c> is reachable only through this
    /// staged call.
    /// </summary>
    ICompensatedContinuation MarkAsCompensated<TStep>() where TStep : IMessage;

    Task MarkAsCompensationFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCancelled<TStep>(Exception? ex = null, CancellationToken cancellationToken = default) where TStep : IMessage;

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
