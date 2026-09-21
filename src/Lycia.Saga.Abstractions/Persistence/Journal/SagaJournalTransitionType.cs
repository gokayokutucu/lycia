// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
namespace Lycia.Saga.Abstractions.Persistence.Journal;

/// <summary>
/// Coarse classification of a canonical saga journal transition. Fine-grained per-step status
/// (including compensation and cancellation of individual steps) lives in the entry's step snapshot,
/// not in this top-level marker.
/// </summary>
public enum SagaJournalTransitionType
{
    /// <summary>The first committed version for this saga.</summary>
    Created,
    /// <summary>An intermediate committed version.</summary>
    Updated,
    /// <summary>The saga data was marked completed by this transition.</summary>
    Completed,
    /// <summary>The saga data was marked failed by this transition.</summary>
    Failed
}
