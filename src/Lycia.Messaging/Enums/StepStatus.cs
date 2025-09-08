// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Messaging.Enums;

/// <summary>
/// Represents the execution status of a step within a saga.
/// Used for idempotency, retry logic, auditing, and compensation triggers.
/// </summary>
public enum StepStatus
{
    /// <summary>
    /// The step has not been executed yet.
    /// </summary>
    None,
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
    CompensationFailed,

    /// <summary>
    /// The step was cancelled and will not proceed further.
    /// </summary>
    Cancelled
}