using Lycia.Messaging.Enums;

namespace Lycia.Saga.Helpers;

public static class SagaStepTransitionHelper
{
    public static bool IsValidStepTransition(StepStatus previous, StepStatus next)
    {
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