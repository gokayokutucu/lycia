// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Common.Messaging;
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Contexts;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Middleware;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Inbox;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Middlewares;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
using Lycia.Saga.Exceptions;
using Lycia.Saga.Helpers;
using Lycia.Saga.Messaging;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lycia.Dispatching;

/// <summary>
/// Responsible for resolving and invoking saga-related handlers for incoming messages.
/// </summary>
public class SagaDispatcher(
    ISagaStore sagaStore,
    ISagaIdGenerator sagaIdGenerator,
    IServiceProvider serviceProvider,
    ILogger<SagaDispatcher> logger)
    : ISagaDispatcher
{
    private async Task DispatchByMessageTypeAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken) where TMessage : IMessage
    {
        if (handlerType == null)
        {
            logger.LogWarning("No handler type resolved for message {MessageType}", typeof(TMessage).Name);
            return;
        }

        var handler = serviceProvider.GetRequiredService(handlerType);
        await InvokeHandlerAsync(handler, message, sagaId, cancellationToken: cancellationToken);
    }

    /// <summary>Dispatches a command or event to the resolved saga handler through the configured middleware pipeline.</summary>
    public async Task DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken) where TMessage : IMessage
    {
             await DispatchByMessageTypeAsync(message, handlerType, sagaId, cancellationToken);
    }

    /// <summary>Dispatches a strongly typed response to its request handler without publishing it as an event.</summary>
    public async Task DispatchAsync<TMessage, TResponse>(TResponse message, Type? handlerType, Guid? sagaId,
        CancellationToken cancellationToken)
        where TMessage : IMessage
        where TResponse : IResponse<TMessage>
    {
        var messageType = message.GetType();

        if (typeof(IResponse<>).IsAssignableFrom(messageType) &&
            (IsEvent(messageType) || IsCommand(messageType)))
        {
            return;
        }

        if (IsSuccessResponse(messageType))
        {
            if (handlerType is null)
                throw new InvalidOperationException($"No handler is registered for response '{messageType.FullName}'.");

            logger.LogInformation("Dispatching {Message} to {Handler}", messageType.Name, handlerType.Name);
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType), message,
                cancellationToken: cancellationToken);
        }
        else if (IsFailResponse(messageType))
        {
            var fail = new FailResponse
            {
                Reason = "An error occurred while handling the message.",
                ExceptionType = message.GetType().Name,
                OccurredAt = DateTime.UtcNow
            };
            logger?.LogInformation("Dispatching {Message} to {Handler}", messageType.Name, handlerType?.Name);
            await InvokeHandlerAsync(serviceProvider.GetServices(handlerType!), message, sagaId, fail,
                cancellationToken);
        }
        else
        {
            await DispatchByMessageTypeAsync(message, handlerType, sagaId, cancellationToken);
        }
    }

    private async Task InvokeHandlerAsync(
        object? handler,
        IMessage message,
        Guid? sagaId = null,
        FailResponse? fail = null, CancellationToken cancellationToken = default)
    {
        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaCompensationCoordinator)) is not ISagaCompensationCoordinator
            compensationCoordinator)
            throw new InvalidOperationException("ISagaCompensationCoordinator not resolved.");


        // SagaId resolution logic
        var messageType = message.GetType();
        var sagaIdProp = messageType.GetProperty("SagaId");

        // Only ISagaStartHandler gets a new SagaId if needed
        var handlerType = handler!.GetType();
        var isStartHandler = handlerType.IsSubclassOfRawGeneric(typeof(ISagaStartHandler<>)) ||
                             handlerType.IsSubclassOfRawGeneric(typeof(ISagaStartHandler<,>));

        if (sagaIdProp != null && sagaIdProp.GetValue(message) is Guid value && value != Guid.Empty)
        {
            sagaId = value;
        }
        else if (isStartHandler)
        {
            sagaId = sagaIdGenerator.Generate();
            // Optionally assign to message property if settable
            if (sagaIdProp != null && sagaIdProp.CanWrite)
                sagaIdProp.SetValue(message, sagaId);
        }
        else if (sagaId is null && sagaIdProp is null)
        {
            // Not a start handler and SagaId missing: throw!
            throw new InvalidOperationException("Missing SagaId on a non-starting message.");
        }
        
        if (!IsSupportedSagaHandler(handlerType)) return;

        var topology = serviceProvider.GetService<IPersistenceTopology>()?.Current;
        var useLocalAtomic = topology?.ResolvedStrategy == PersistenceExecutionStrategy.LocalAtomic;
        var sessionAccessor = serviceProvider.GetService<ILyciaPersistenceSessionAccessor>();
        ILyciaPersistenceSession? ownedSession = null;
        ILyciaPersistenceSession? previousSession = null;

        if (useLocalAtomic)
        {
            if (sessionAccessor == null)
                throw new InvalidOperationException("Local atomic persistence requires a scoped session accessor.");
            if (sessionAccessor.Current != null)
                throw new InvalidOperationException("A nested Lycia persistence session is not supported.");

            var sessionFactory = serviceProvider.GetRequiredService<ILyciaPersistenceSessionFactory>();
            ownedSession = await sessionFactory.BeginAsync(cancellationToken);
            if (!ownedSession.SupportsAtomicTransactions)
            {
                await ownedSession.DisposeAsync();
                throw new InvalidOperationException(
                    "The resolved LocalAtomic topology did not provide an atomic persistence session.");
            }

            previousSession = sessionAccessor.Current;
            sessionAccessor.Current = ownedSession;
        }

        var inboxStore = serviceProvider.GetService<IInboxStore>();
        var sagaContextAccessor = serviceProvider.GetService<ISagaContextAccessor>();
        var previous = sagaContextAccessor?.Current;

        // Framework-managed correlation metadata for the canonical journal (Phase 6). Never set by
        // saga handler code. Cleared in finally like the other scoped accessors above.
        var journalContextAccessor = serviceProvider.GetService<ISagaJournalContextAccessor>();
        var previousJournalContext = journalContextAccessor?.Current;
        if (journalContextAccessor != null)
        {
            journalContextAccessor.Current = new SagaJournalTransitionContext
            {
                MessageId = message.MessageId,
                RequestId = message is IRequestRoutingMetadata routing ? routing.RequestId : null,
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
                ParentMessageId = message.ParentMessageId == Guid.Empty ? null : message.ParentMessageId,
                ApplicationId = message.ApplicationId,
                HandlerType = handlerType.GetSimplifiedQualifiedName(),
                MessageType = messageType.GetSimplifiedQualifiedName()
            };
        }

        try
        {
            // Inbox is optional. Under LocalAtomic its claim, handler persistence, Outbox capture,
            // and completion all use the session installed above and commit as one unit.
            if (inboxStore != null)
            {
                var beginResult = await inboxStore.TryBeginAsync(message.MessageId, handlerType, cancellationToken);
                if (beginResult != InboxBeginResult.Started)
                {
                    logger.LogInformation(
                        "Inbox: message {MessageId} for handler {HandlerType} is already {InboxResult}; skipping duplicate execution.",
                        message.MessageId, handlerType.Name, beginResult);
                    if (ownedSession != null) await ownedSession.RollbackAsync(cancellationToken);
                    return;
                }
            }

            var createdContext = await SagaContextFactory.InitializeForHandlerAsync(
                handler,
                sagaId!.Value,
                message,
                eventBus,
                sagaStore,
                sagaIdGenerator,
                compensationCoordinator,
                serviceProvider,
                cancellationToken);

            var middlewares = serviceProvider.GetServices<ISagaMiddleware>();
            var orderedTypes = serviceProvider.GetService<IReadOnlyList<Type>>();
            var pipeline = new Middleware.SagaMiddlewarePipeline(middlewares, serviceProvider, orderedTypes);
            var ctx = new SagaContextInvocationContext
            {
                Message = message,
                SagaContext = createdContext as ISagaContext,
                HandlerType = handlerType,
                SagaId = sagaId,
                ApplicationId = message.ApplicationId,
                CancellationToken = cancellationToken
            };

            if (sagaContextAccessor != null)
                sagaContextAccessor.Current = createdContext as ISagaContext;
            await pipeline.InvokeAsync(ctx, () => HandleSagaAsync(message, handler, handlerType, cancellationToken));

            if (inboxStore != null)
                await inboxStore.MarkCompletedAsync(message.MessageId, handlerType, cancellationToken);

            if (ownedSession != null)
                await ownedSession.CommitAsync(cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException)
        {
            // Never mark the Inbox failed or rerun business logic when COMMIT may have succeeded.
            // Durable identities are the recovery authority for this indeterminate outcome.
            throw;
        }
        catch (Exception ex)
        {
            if (ownedSession != null)
            {
                await ownedSession.RollbackAsync(cancellationToken);
                sessionAccessor!.Current = previousSession;
            }

            if (inboxStore != null)
                await inboxStore.MarkFailedAsync(message.MessageId, handlerType,
                    new SagaStepFailureInfo("Handler execution failed", ex.GetType().Name, ex.ToString()), cancellationToken);
            throw;
        }
        finally
        {
            if (sagaContextAccessor != null) sagaContextAccessor.Current = previous;
            if (journalContextAccessor != null) journalContextAccessor.Current = previousJournalContext;
            if (sessionAccessor != null) sessionAccessor.Current = previousSession;
            if (ownedSession != null) await ownedSession.DisposeAsync();
        }
    }

    private async Task HandleSagaAsync(IMessage message, object? handler, Type handlerType, CancellationToken cancellationToken)
    {
        if (handler == null) return;

        // Call HandleStartAsync
        try
        {
            var sagaId = GetSagaId(message);

            var msgType = message.GetType();
            var methodName = FindMethodName(msgType);

            var delegateMethod = 
                HandlerDelegateHelper.GetHandlerDelegate(handlerType, methodName, msgType);
            await delegateMethod(handler, message, cancellationToken);

            await ValidateSagaStepCompletionAsync(message, handlerType, sagaId);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to invoke saga handler dynamically: {Message}", ex.Message);
            throw new SagaDispatchException($"Failed to invoke HandleAsync dynamically: {ex.Message}", ex);
        }
    }

    private static string FindMethodName(Type msgType)
    {
        if (typeof(FailedEventBase).IsAssignableFrom(msgType))
        {
            // Choreography: failed events should invoke ISagaCompensationHandler<TFailed>.CompensateAsync
            return "CompensateAsync";
        }
        
        if (msgType.IsSuccessResponse())
        {
           return "HandleSuccessResponseAsync";
        }

        return "HandleAsyncInternal";
    }

    private Guid GetSagaId(IMessage message)
    {
        Guid sagaId;
        var sagaIdProp = message.GetType().GetProperty("SagaId");
        if (sagaIdProp != null && sagaIdProp.GetValue(message) is Guid value && value != Guid.Empty)
        {
            sagaId = value;
        }
        else
        {
            sagaId = sagaIdGenerator.Generate();
        }

        return sagaId;
    }

    private async Task ValidateSagaStepCompletionAsync(IMessage message, Type handlerType, Guid sagaId)
    {
        var stepTypeToCheck = message.GetType();

        // Use status to validate all terminal outcomes, not just "Completed"
        var status = await sagaStore.GetStepStatusAsync(
            sagaId,
            message.MessageId,
            stepTypeToCheck,
            handlerType);

        var isTerminal = status is StepStatus.Completed or StepStatus.Failed or StepStatus.Compensated;

        if (!isTerminal)
        {
            logger.LogWarning(
                "Step for {Step} has status {Status} - expected Completed/Failed/Compensated",
                stepTypeToCheck.Name,
                status);
        }
    }
    
    private static bool IsSupportedSagaHandler(Type t) =>
        t.IsSubclassOfRawGenericBase(typeof(CoordinatedSagaHandler<,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(CoordinatedResponsiveSagaHandler<,,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedResponsiveSagaHandler<,,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,>)) ||
        t.IsSubclassOfRawGenericBase(typeof(ReactiveSagaHandler<>)) ||
        t.IsSubclassOfRawGenericBase(typeof(StartReactiveSagaHandler<>));

    private static bool IsSuccessResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISuccessResponse<>));

    private static bool IsFailResponse(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IFailResponse<>));

    private static bool IsCommand(Type type) => typeof(ICommand).IsAssignableFrom(type);
    private static bool IsEvent(Type type) => typeof(IEvent).IsAssignableFrom(type);
}
