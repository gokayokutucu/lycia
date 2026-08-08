// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Inbox;

/// <summary>Durable processing state of an incoming message for a specific handler, as tracked by <see cref="IInboxStore"/>.</summary>
public enum InboxMessageStatus
{
    /// <summary>No inbox record exists for this (MessageId, HandlerType) pair yet.</summary>
    None,

    /// <summary>The handler has claimed this message and is currently executing.</summary>
    Processing,

    /// <summary>The handler finished processing this message successfully.</summary>
    Completed,

    /// <summary>The handler attempted to process this message and failed.</summary>
    Failed
}
