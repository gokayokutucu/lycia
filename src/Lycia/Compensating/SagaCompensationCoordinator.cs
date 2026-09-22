// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using System.Diagnostics;
using System.Text;
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Compensating;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Helpers;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Lycia.Compensating;

/// <summary>
/// Coordinates the compensation of saga steps in the event of a failure during
/// a saga's execution. This class is responsible for invoking compensation
/// logic for failed saga steps and their hierarchical relationships.
/// </summary>
public class SagaCompensationCoordinator(
    IServiceProvider serviceProvider,
    ISagaIdGenerator sagaIdGenerator,
    IMessageSerializer serializer,
    IOptions<CompensationWorkerOptions>? compensationOptions = null)
    : ISagaCompensationCoordinator
{
    private readonly CompensationWorkerOptions _compensationOptions = compensationOptions?.Value ?? new CompensationWorkerOptions();
    /// <summary>
    /// Executes the compensation logic for a specific saga step that has encountered an error.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga.</param>
    /// <param name="failedStepType">The type of the saga step that failed and requires compensation.</param>
    /// <param name="handlerType">The type of the compensation handler responsible for handling the step's failure.</param>
    /// <param name="message">The message associated with the failed saga step.</param>
    /// <param name="failInfo">Additional failure information related to the saga step.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the asynchronous compensation operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required services such as IEventBus or ISagaStore cannot be resolved.</exception>
    public async Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message,
        SagaStepFailureInfo? failInfo, CancellationToken cancellationToken = default)
    {
        if (handlerType == null) return;

        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore
            sagaStore)
            throw new InvalidOperationException("ISagaStore not resolved.");

        var stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (IsStepAlreadyInStatus(stepKeyValuePair, StepStatus.Failed, StepStatus.Compensated,
                StepStatus.CompensationFailed))
            return;

        await sagaStore.LogStepAsync(sagaId, message.MessageId, message.ParentMessageId, failedStepType,
            StepStatus.Failed, handlerType, message, failInfo, cancellationToken);
        ReportStepFailure(sagaId, failedStepType, handlerType, message, failInfo);

        stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (!stepKeyValuePair.HasValue) return;
        var step = stepKeyValuePair.Value;

        var stepType = Type.GetType(step.Key.stepType);
        var payloadType = Type.GetType(step.Value.MessageTypeName);
        if (stepType == null || payloadType == null) return;

        var (headers, serCtx) = serializer.CreateContextFor(payloadType);
        var messageObject =
            serializer.Deserialize(Encoding.UTF8.GetBytes(step.Value.MessagePayload), headers, serCtx)
            ?? JsonConvert.DeserializeObject(step.Value.MessagePayload, payloadType);
        if (messageObject == null) return;

        // If no handler found, try to find candidate handlers using the new method
        var handler = FindCompensationHandler(handlerType, stepType);

        await InvokeCompensationHandlerAsync(sagaId, handler, stepType, sagaStore, messageObject, eventBus, cancellationToken);
    }


    /// <summary>
    /// Makes a failed step visible in logs and traces. The saga handler base classes catch the business
    /// exception and record it as a failed step instead of letting it propagate, so the dispatch itself
    /// returns normally: without this, the only trace of the failure would be the durable step record,
    /// and the handler span would report the step as completed.
    /// </summary>
    private void ReportStepFailure(Guid sagaId, Type failedStepType, Type handlerType, IMessage message,
        SagaStepFailureInfo? failInfo)
    {
        var reason = failInfo?.Reason ?? "Saga step failed";
        var exceptionType = failInfo?.ExceptionType;
        var exceptionMessage = FirstLine(failInfo?.ExceptionDetail);

        serviceProvider.GetService<ILogger<SagaCompensationCoordinator>>()?.LogWarning(
            "Saga step {StepType} failed in handler {Handler} [SagaId={SagaId}, MessageId={MessageId}]: {Reason} " +
            "{ExceptionType} {ExceptionMessage} The failure is recorded and compensation is starting.",
            failedStepType.Name, handlerType.Name, sagaId, message.MessageId, reason, exceptionType, exceptionMessage);

        var activity = Activity.Current;
        if (activity == null) return;
        activity.SetStatus(ActivityStatusCode.Error, exceptionMessage ?? reason);
        activity.SetTag("lycia.saga.step.status", nameof(StepStatus.Failed));
        activity.SetTag("lycia.saga.step.failure_reason", reason);
        if (exceptionType != null) activity.SetTag("exception.type", exceptionType);
        if (exceptionMessage != null) activity.SetTag("exception.message", exceptionMessage);
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var newLine = text!.IndexOfAny(['\r', '\n']);
        return newLine < 0 ? text : text.Substring(0, newLine);
    }

    /// <summary>
    /// Marks the current step compensated and, if it has a logical parent, durably requires and
    /// immediately attempts propagating compensation to that parent. This is what
    /// <c>ContinueCompensation().ThenMarkAsCompensated&lt;TStep&gt;().ThenBubbleUp(ct)</c> ultimately calls,
    /// through the internal <c>IBubbleUpCompensationPrimitive</c> execution primitive on the saga context.
    /// </summary>
    /// <remarks>
    /// Marking the current step compensated and requiring parent propagation are two independently
    /// durable facts (see <see cref="Lycia.Common.SagaSteps.CompensationPropagationIntent"/>): the step's
    /// own <see cref="StepStatus.Compensated"/> status is never treated as proof that propagation to the
    /// parent completed, or even started. <paramref name="cancellationToken"/> governs the whole call, but
    /// once the propagation requirement is durably created, cancelling the immediate attempt (or the
    /// process crashing during it) leaves the requirement intact for <c>CompensationWorker</c> to resume -
    /// it never erases it.
    /// </remarks>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="stepType">The type of the step being compensated.</param>
    /// <param name="handlerType">The handler type that owns this step.</param>
    /// <param name="message">The message of the current step.</param>
    /// <param name="cancellationToken">Observed before the propagation requirement is durably created; never used to erase it afterward.</param>
    public async Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore sagaStore)
            throw new InvalidOperationException("ISagaStore not resolved.");

        // A step already durably CompensationFailed is a genuine terminal failure, not a retry of a
        // successful compensation - it must never be silently overwritten as Compensated (an illegal
        // transition the store itself would reject) and must never propagate as if it had succeeded. This
        // is the one status check CompensateParentAsync still makes; it is narrower than the removed guard,
        // which also (wrongly) blocked re-propagation for a step already Compensated - see the KnownGap
        // regression this replaces, and CompensationContinuationTests for the closed-gap proof.
        var currentStatus = await sagaStore.GetStepStatusAsync(sagaId, message.MessageId, stepType, handlerType)
            .ConfigureAwait(false);
        if (currentStatus == StepStatus.CompensationFailed)
            return;

        // Idempotent: the provider's own step-transition validation treats re-logging the same status
        // for the same message as a safe no-op, so no separate "already compensated" guard is needed
        // here - unlike the removed design, that guard must never be what decides whether propagation is
        // attempted (see the KnownGap regression this replaces).
        await sagaStore.LogStepAsync(sagaId, message.MessageId, message.ParentMessageId, stepType,
            StepStatus.Compensated, handlerType, message, (Exception?)null, cancellationToken);

        var parentMessageId = message.ParentMessageId;
        if (parentMessageId == Guid.Empty)
            return; // Root step: no logical parent, no propagation requirement.

        var owner = $"inline:{Guid.NewGuid():N}";
        var claim = await sagaStore.EnsureAndClaimCompensationPropagationAsync(sagaId, message.MessageId,
            parentMessageId, owner, _compensationOptions.RecoveryTimeout, _compensationOptions.MaxAttempts,
            cancellationToken).ConfigureAwait(false);

        if (claim.Outcome != CompensationPropagationClaimOutcome.Claimed)
            return; // AlreadyCompleted / ClaimedByAnother / AttemptsExhausted: nothing more to do right now.

        // The durable requirement is committed at this point. A cancellation or exception from here on
        // propagates to the caller as usual, but never un-claims or deletes the intent: CompensationWorker
        // recovers it once its lease (RecoveryTimeout) passes.
        await AttemptPropagationAsync(sagaId, message.MessageId, eventBus, sagaStore, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Performs one propagation attempt for an already-claimed edge: resolves the logical parent from the
    /// current step snapshot (skipping one orchestrator response hop where applicable), deserializes its
    /// original message, resolves its compensation handler, and invokes it. Used both by the immediate
    /// attempt in <see cref="CompensateParentAsync"/> and by <c>CompensationWorker</c> recovery passes, so
    /// both paths share identical lineage-resolution and invocation behavior.
    /// </summary>
    /// <remarks>
    /// A structurally unresolvable edge (the parent step record is missing, its type cannot be resolved,
    /// or its payload cannot be deserialized) is marked <see cref="CompensationPropagationStatus.Failed"/>
    /// immediately rather than retried - retrying cannot fix a lineage/serialization problem. A handler
    /// invocation that throws is left to the caller: the edge stays <see cref="CompensationPropagationStatus.Claimed"/>
    /// and becomes reclaimable after the configured recovery window, which is the bounded-retry path.
    /// </remarks>
    internal async Task AttemptPropagationAsync(Guid sagaId, Guid childMessageId, IEventBus eventBus,
        ISagaStore sagaStore, CancellationToken cancellationToken)
    {
        var childStep = await sagaStore.GetSagaHandlerStepAsync(sagaId, childMessageId).ConfigureAwait(false);
        if (!childStep.HasValue)
        {
            await FailUnresolvableAsync(sagaStore, sagaId, childMessageId,
                "The compensated child step record was not found.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId).ConfigureAwait(false);
        var parentMessageId = childStep.Value.Value.ParentMessageId;
        var parentKvp = FindLogicalParentFromSnapshot(steps, parentMessageId);
        if (!parentKvp.HasValue)
        {
            await FailUnresolvableAsync(sagaStore, sagaId, childMessageId,
                "No logical parent step record could be resolved for this saga step lineage.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var parentStep = parentKvp.Value;
        var parentStepType = Type.GetType(parentStep.Key.stepType);
        if (parentStepType == null)
        {
            await FailUnresolvableAsync(sagaStore, sagaId, childMessageId,
                $"The parent step type '{parentStep.Key.stepType}' could not be resolved.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var (headers, serCtx) = serializer.CreateContextFor(parentStepType);
        var messageObject =
            serializer.Deserialize(Encoding.UTF8.GetBytes(parentStep.Value.MessagePayload), headers, serCtx)
            ?? JsonConvert.DeserializeObject(parentStep.Value.MessagePayload, parentStepType);
        if (messageObject == null)
        {
            await FailUnresolvableAsync(sagaStore, sagaId, childMessageId,
                "The parent step's message payload could not be deserialized.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var parentHandlerType = Type.GetType(parentStep.Key.handlerType);
        var handler = FindCompensationHandler(parentHandlerType, parentStepType);

        // If this throws, the edge intentionally stays Claimed - see the remarks above.
        await InvokeCompensationHandlerAsync(sagaId, handler, parentStepType, sagaStore, messageObject, eventBus,
            cancellationToken).ConfigureAwait(false);

        await sagaStore.MarkCompensationPropagationCompletedAsync(sagaId, childMessageId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task FailUnresolvableAsync(ISagaStore sagaStore, Guid sagaId, Guid childMessageId, string reason,
        CancellationToken cancellationToken) =>
        sagaStore.MarkCompensationPropagationFailedAsync(sagaId, childMessageId,
            new SagaStepFailureInfo(reason, null, null), cancellationToken);

    private async Task InvokeCompensationHandlerAsync(Guid sagaId, object? handler, Type stepType, ISagaStore sagaStore,
        object messageObject, IEventBus eventBus, CancellationToken cancellationToken = default)
    {
        if (handler == null)
            return;

        var handlerTypeActual = handler.GetType();

        var handlerBaseType = handler.GetType().BaseType;
        var handlerGenericDef =
            handlerBaseType is { IsGenericType: true }
                ? handlerBaseType.GetGenericTypeDefinition()
                : null;

        if (handlerGenericDef == typeof(StartReactiveSagaHandler<>)
            || handlerGenericDef == typeof(StartCoordinatedSagaHandler<,>)
            || handlerGenericDef == typeof(StartCoordinatedResponsiveSagaHandler<,,>)
            || handlerGenericDef == typeof(ReactiveSagaHandler<>)
            || handlerGenericDef == typeof(CoordinatedResponsiveSagaHandler<,,>)
            || handlerGenericDef == typeof(CoordinatedSagaHandler<,>))
        {
            var delegateMethod = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsyncInternal", stepType);

            await SagaContextFactory.InitializeForHandlerAsync(
                handler,
                sagaId,
                messageObject,
                eventBus,
                sagaStore,
                sagaIdGenerator,
                this, 
                serviceProvider,
                cancellationToken);

            await delegateMethod(handler, messageObject, cancellationToken);
        }
        else
        {
            var implementsCompensationHandler = handlerTypeActual.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(ISagaCompensationHandler<>) &&
                i.GetGenericArguments()[0].FullName == stepType.FullName);

            if (implementsCompensationHandler)
            {
                var delegateMethod = HandlerDelegateHelper.GetHandlerDelegate(handlerTypeActual, "CompensateAsync", stepType);

                await SagaContextFactory.InitializeForHandlerAsync(
                    handler,
                    sagaId,
                    messageObject,
                    eventBus,
                    sagaStore,
                    sagaIdGenerator,
                    this, 
                    serviceProvider,
                    cancellationToken);

                await delegateMethod(handler, messageObject, cancellationToken);
            }
        }
    }

    // Optional: future-proof bounded climb
    private static KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>?
        FindLogicalParentFromSnapshot(
            IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata> stepsSnapshot,
            Guid? startParentMessageId,
            int maxHops = 1) // 1 is sufficient for today; supports >1 for future use
    {
        if (!startParentMessageId.HasValue || startParentMessageId.Value == Guid.Empty)
            return null;

        var byMsgId = BuildByMessageIdIndex(stepsSnapshot);

        var currentId = startParentMessageId.Value;
        var hopLimit = Math.Max(1, maxHops);

        for (var hop = 0; hop < hopLimit; hop++)
        {
            if (!TryGetEntry(byMsgId, currentId, out var kv))
                return null;

            if (!ShouldSkipOrchestratorHop(kv, out var nextId) || !nextId.HasValue || nextId.Value == Guid.Empty)
                return kv;

            currentId = nextId.Value;
        }

        return TryGetEntry(byMsgId, currentId, out var last) ? last : (KeyValuePair<(string, string, string), SagaStepMetadata>?)null;
    }

    /// <summary>
    /// Builds a duplicate-safe index keyed by MessageId. If multiple entries share the same messageId
    /// (e.g., the same message handled by different handlers), prefers the one with the latest RecordedAt.
    /// </summary>
    private static Dictionary<Guid, KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>>
        BuildByMessageIdIndex(IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata> stepsSnapshot)
    {
        var byMsgId = new Dictionary<Guid, KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>>();

        foreach (var kvp in stepsSnapshot)
        {
            if (!Guid.TryParse(kvp.Key.messageId, out var mid))
                continue; // ignore malformed ids defensively

            if (byMsgId.TryGetValue(mid, out var existing))
            {
                var existingTs = existing.Value.RecordedAt;
                var currentTs  = kvp.Value.RecordedAt;
                if (currentTs >= existingTs)
                    byMsgId[mid] = kvp; // prefer newer
            }
            else
            {
                byMsgId[mid] = kvp;
            }
        }

        return byMsgId;
    }

    /// <summary>
    /// Tries to get a step entry from the index by message id.
    /// </summary>
    private static bool TryGetEntry(
        Dictionary<Guid, KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>> index,
        Guid messageId,
        out KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata> entry)
    {
        return index.TryGetValue(messageId, out entry);
    }

    /// <summary>
    /// Determines whether the current entry represents an orchestrator response hop that should be skipped.
    /// If so, returns the next (grandparent) message id via <paramref name="nextParentId"/>.
    /// </summary>
    private static bool ShouldSkipOrchestratorHop(
        KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata> entry,
        out Guid? nextParentId)
    {
        nextParentId = null;

        var stepType = Type.GetType(entry.Key.stepType);
        var handlerType = Type.GetType(entry.Key.handlerType);

        // If the type cannot be resolved, do not skip; treat current as the logical parent.
        if (stepType == null || handlerType == null)
            return false;

        var handledByOrchestrator = IsOrchestratorHandler(handlerType);
        var isResponse = stepType.IsSubclassOfResponseBase();

        if (!handledByOrchestrator || !isResponse) return false;
        nextParentId = entry.Value.ParentMessageId;
        return true;

    }

    private object? FindCompensationHandler(Type? handlerType, Type stepType)
    {
        if (handlerType == null) return null;

        var interfaceType = typeof(ISagaCompensationHandler<>).MakeGenericType(stepType);

        var interfaceHandlers = serviceProvider.GetServices(interfaceType).Cast<object>();
        var concreteHandler = serviceProvider.GetService(handlerType);

        var allHandlers = concreteHandler != null
            ? interfaceHandlers.Concat([concreteHandler])
            : interfaceHandlers;

        return allHandlers
            .DistinctByKey(h => h.GetType())
            .FirstOrDefault(t => t?.GetType().FullName == handlerType.FullName);
    }

    private static bool IsStepAlreadyInStatus(
        KeyValuePair<(string stepType, string handlerType, string messageId), SagaStepMetadata>? stepKeyValuePair,
        params StepStatus[] statuses)
    {
        return stepKeyValuePair.HasValue && statuses.Contains(stepKeyValuePair.Value.Value.Status);
    }
    
    private static bool IsOrchestratorHandler(Type? t)
        => t != null && (
            t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedResponsiveSagaHandler<,,>)) ||
            t.IsSubclassOfRawGenericBase(typeof(StartCoordinatedSagaHandler<,>))
        );
}