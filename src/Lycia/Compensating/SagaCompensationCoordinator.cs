// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using System.Text;
using Lycia.Common.Enums;
using Lycia.Common.SagaSteps;
using Lycia.Extensions;
using Lycia.Helpers;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Handlers;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Helpers;
using Lycia.Saga.Messaging.Handlers;
using Microsoft.Extensions.DependencyInjection;
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
    IMessageSerializer serializer)
    : ISagaCompensationCoordinator
{
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
            StepStatus.Failed, handlerType, message, failInfo);

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
    /// Compensates the parent saga step of the specified step type.
    /// </summary>
    /// <param name="sagaId">The identifier of the saga.</param>
    /// <param name="stepType">The type of the step whose parent is to be compensated.</param>
    /// <param name="handlerType"></param>
    /// <param name="message">The message of the current step</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message, CancellationToken cancellationToken = default)
    {
        if (serviceProvider.GetService(typeof(IEventBus)) is not IEventBus eventBus)
            throw new InvalidOperationException("IEventBus not resolved.");

        if (serviceProvider.GetService(typeof(ISagaStore)) is not ISagaStore
            sagaStore)
            throw new InvalidOperationException("ISagaStore not resolved.");

        var stepKeyValuePair = await sagaStore.GetSagaHandlerStepAsync(sagaId, message.MessageId);
        if (IsStepAlreadyInStatus(stepKeyValuePair, StepStatus.Compensated, StepStatus.CompensationFailed))
            return;
        // Log the step as failed before compensating
        await sagaStore.LogStepAsync(sagaId, message.MessageId, message.ParentMessageId, stepType,
            StepStatus.Compensated, handlerType, message, (Exception?)null);
        
        var steps = await sagaStore.GetSagaHandlerStepsAsync(sagaId);
        
        if (steps.All(kv => kv.Key.messageId != message.MessageId.ToString()))
            return;

        // Get the parent message ID from the current message
        var parentMessageId = message.ParentMessageId;
        if (parentMessageId == Guid.Empty)
            return;

        // Find the logical parent (may skip central orchestrator response hop)
        var parentKvp = FindLogicalParentFromSnapshot(steps, parentMessageId);
        if (!parentKvp.HasValue) return;

        var parentStep = parentKvp.Value;
        var parentStepType = Type.GetType(parentKvp.Value.Key.stepType);
        if (parentStepType == null)
            return;

        var (headers, serCtx) = serializer.CreateContextFor(parentStepType);
        var messageObject =
            serializer.Deserialize(Encoding.UTF8.GetBytes(parentStep.Value.MessagePayload), headers, serCtx)
            ?? JsonConvert.DeserializeObject(parentStep.Value.MessagePayload, parentStepType);
        if (messageObject == null)
            return;

        var parentHandlerType = Type.GetType(parentStep.Key.handlerType);
        // Find the compensation handler for the parent step type
        var handler =
            FindCompensationHandler(parentHandlerType, parentStepType);

        await InvokeCompensationHandlerAsync(sagaId, handler, parentStepType, sagaStore, messageObject, eventBus, cancellationToken);
    }

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