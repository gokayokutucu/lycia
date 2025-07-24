using System.Collections.Concurrent;
using Lycia.Messaging.Enums;
using Lycia.Saga.Enums;
using Lycia.Saga.Exceptions;

namespace Lycia.Saga.Helpers;

public static class SagaStepHelper
{
    public static SagaStepValidationInfo ValidateSagaStepTransition(Guid messageId,
        Guid? parentMessageId,
        StepStatus currentStatus,
        IEnumerable<SagaStepMetadata> steps,
        string stepKey,
        SagaStepMetadata newMeta,
        SagaStepMetadata? existingMeta)
    {
        // 1. Transition and idempotency

        var previousStatus = existingMeta?.Status ?? StepStatus.None;

        if (previousStatus == currentStatus)
        {
            // Same status and same payload
            if (existingMeta != null && existingMeta.IsIdempotentWith(newMeta))
                return new SagaStepValidationInfo(SagaStepValidationResult.Idempotent);

            // Same status but different payload
            return new SagaStepValidationInfo(
                SagaStepValidationResult.DuplicateWithDifferentPayload,
                $"Duplicate update with differing payload for stepKey: {stepKey}");
        }

        // Normal state machine check
        if (!IsValidStepTransition(previousStatus, currentStatus))
            return new SagaStepValidationInfo(
                SagaStepValidationResult.InvalidTransition,
                $"Illegal StepStatus transition: {previousStatus} -> {currentStatus} for {stepKey}");


        // 2. Chain loop check
        if (HasCircularChain(steps, messageId, parentMessageId))
        {
            return new SagaStepValidationInfo(
                SagaStepValidationResult.CircularChain,
                "Circular parent-child chain detected in saga steps.");
        }

        // 3. If we reach here, it's a valid transition
        return new SagaStepValidationInfo(SagaStepValidationResult.ValidTransition);
    }

    private static bool HasCircularChain(IEnumerable<SagaStepMetadata> steps, Guid currentMessageId,
        Guid? parentMessageId)
    {
        var seen = new HashSet<Guid>();
        var parentId = parentMessageId ?? Guid.Empty; //1
        seen.Add(currentMessageId); // 2

        var allSteps = steps.ToDictionary(s => s.MessageId);

        if (!allSteps.ContainsKey(currentMessageId))
        {
            allSteps.Add(currentMessageId, SagaStepMetadata.Build(
                StepStatus.None,
                currentMessageId,
                parentMessageId,
                string.Empty,
                string.Empty,
                null));
        }

        while (parentId != Guid.Empty) // 1
        {
            if (!allSteps.TryGetValue(parentId,
                    out var parentStep)) // If the step id number 1 which not found in the steps break
                break;

            if (!seen.Add(parentId))
            {
                return true;
            }

            parentId = parentStep.ParentMessageId ?? Guid.Empty;
        }

        return false; // No circular references found
    }

    private static bool IsValidStepTransition(
        StepStatus previous,
        StepStatus next)
        => previous switch
        {
            StepStatus.None => true,
            StepStatus.Started => next is StepStatus.Completed or StepStatus.Failed,
            StepStatus.Completed => next is StepStatus.Compensated or StepStatus.CompensationFailed,
            StepStatus.Failed => next is StepStatus.Compensated or StepStatus.CompensationFailed,
            StepStatus.Compensated => false,
            StepStatus.CompensationFailed => false,
            _ => false
        };
}