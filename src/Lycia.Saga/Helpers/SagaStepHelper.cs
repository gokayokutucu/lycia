using Lycia.Messaging.Enums;

namespace Lycia.Saga.Helpers;

public static class SagaStepHelper
{
    // --------------------------------------------------------------
    // Parent/Child Chain Traversal (Context Chain) Note
    // --------------------------------------------------------------
    // This Coordinator implementation relies on traversing the parent/child
    // step chain by following ParentMessageId and MessageId links in each step.
    // To ensure the context chain works correctly:
    //
    // - The ParentMessageId must be set accurately in SagaStepMetadata.
    // - The parent information must be maintained whenever logging a saga step.
    // - The helper SagaStepHelper.GetParentChain() is used to retrieve the parent lineage.
    //
    // This enables compensation and stack tracing/debugging scenarios to
    // reliably reconstruct the sequence of parent steps.
    //
    // Note: In multi-branch or concurrent saga scenarios, the parent-child
    // chain is uniquely maintained using MessageId (no risk of collision or
    // circular references). Each step always points to its parent,
    // and compensation traverses the chain upward as needed.
    //
    // (For implementation details, see: SagaStepHelper.GetParentChain())
    // --------------------------------------------------------------
    public static List<SagaStepMetadata> GetParentChain(IEnumerable<SagaStepMetadata> steps, Guid messageId)
    {
        var all = steps.ToList();
        var chain = new List<SagaStepMetadata>();
        var current = all.FirstOrDefault(x => x.MessageId == messageId);
        while (current != null)
        {
            chain.Add(current);
            if (current.ParentMessageId == Guid.Empty)
                break;
            current = all.FirstOrDefault(x => x.MessageId == current.ParentMessageId);
        }
        return chain;
    }
    
    public static bool IsValidStepTransition(StepStatus previous, StepStatus next)
    {
        // Allow idempotent transitions (same status, regardless of payload)
        if (previous == next)
            return true;

        // Only allow state progressions, not regressions (except idempotent/same)
        return previous switch
        {
            StepStatus.None => next == StepStatus.Started,
            StepStatus.Started => next is StepStatus.Completed or StepStatus.Failed,
            StepStatus.Completed => next is StepStatus.Compensated or StepStatus.CompensationFailed,
            StepStatus.Failed => next is StepStatus.Compensated or StepStatus.CompensationFailed,
            StepStatus.Compensated => false, // final
            StepStatus.CompensationFailed => false, // final
            _ => false
        };
    }
}