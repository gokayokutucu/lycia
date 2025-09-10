// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

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
    Task Publish<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent;
    
    Task Publish<T>(T @event, Type? handlerType, CancellationToken cancellationToken = default) where T : IEvent;

    ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken cancellationToken = default)
        where TNextStep : IEvent;

    ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken cancellationToken = default)
        where TNextStep : ICommand;

    Task Compensate<T>(T @event, CancellationToken cancellationToken = default) where T : FailedEventBase;

    Task MarkAsComplete<TStep>() where TStep : IMessage;
    Task MarkAsFailed<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(Exception? ex, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsFailed<TStep>(FailResponse fail, CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensated<TStep>() where TStep : IMessage;
    Task CompensateAndBubbleUp<TStep>(CancellationToken cancellationToken = default) where TStep : IMessage;
    Task MarkAsCompensationFailed<TStep>() where TStep : IMessage;    
    Task MarkAsCompensationFailed<TStep>(Exception? ex) where TStep : IMessage;
    Task MarkAsCancelled<TStep>(Exception? ex) where TStep : IMessage;

    Task<bool> IsAlreadyCompleted<T>() where T : IMessage;
}

public interface ISagaContext<TInitialMessage, out TSagaData> : ISagaContext<TInitialMessage>
    where TSagaData : SagaData
    where TInitialMessage : IMessage
{
    TSagaData Data { get; }

    new ISagaStepFluent PublishWithTracking<TNextStep>(TNextStep nextEvent, CancellationToken cancellationToken = default)
        where TNextStep : IEvent;

    new ISagaStepFluent SendWithTracking<TNextStep>(TNextStep nextCommand, CancellationToken cancellationToken = default)
        where TNextStep : ICommand;
}