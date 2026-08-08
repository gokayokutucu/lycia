// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Inbox;

/// <summary>Outcome of <see cref="IInboxStore.TryBeginAsync"/>, telling the caller whether it won the right to process the message.</summary>
public enum InboxBeginResult
{
    /// <summary>No prior record existed; the caller has claimed this message and must process it now.</summary>
    Started,

    /// <summary>Another delivery is already processing this exact (MessageId, HandlerType) pair. Safe to skip.</summary>
    AlreadyProcessing,

    /// <summary>This exact (MessageId, HandlerType) pair already completed successfully. Safe no-op.</summary>
    AlreadyCompleted,

    /// <summary>This exact (MessageId, HandlerType) pair already failed. The caller decides whether to retry.</summary>
    AlreadyFailed
}
