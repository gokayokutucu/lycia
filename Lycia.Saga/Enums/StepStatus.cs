namespace Lycia.Saga.Enums;

/// <summary>
/// Represents the execution status of a step within a saga.
/// Used for idempotency, retry logic, auditing, and compensation triggers.
/// </summary>
public enum StepStatus
{
    /// <summary>
    /// The step has been started but not yet completed.
    /// </summary>
    Started,

    /// <summary>
    /// The step was completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The step failed and may require compensation.
    /// </summary>
    Failed,

    /// <summary>
    /// The step was compensated (rolled back).
    /// </summary>
    Compensated,

    /// <summary>
    /// The step was failed in compensation.
    /// </summary>
    CompensationFailed
}