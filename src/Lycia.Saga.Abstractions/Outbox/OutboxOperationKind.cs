// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>Identifies the event-bus semantic represented by a durable outgoing envelope.</summary>
public enum OutboxOperationKind
{
    /// <summary>Routes a command to its owning application.</summary>
    Send,

    /// <summary>Broadcasts an event to its subscribers.</summary>
    Publish,

    /// <summary>Routes a response to the request's response endpoint.</summary>
    Respond
}
