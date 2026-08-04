// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Initializes transport-independent request/reply metadata.</summary>
public static class RequestRouting
{
    /// <summary>
    /// Ensures a command has a request identifier and the sending logical application as its reply endpoint.
    /// </summary>
    public static void Prepare(ICommand command, string? applicationId)
    {
        if (!(command is IRequestRoutingMetadata request)) return;
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new InvalidOperationException("ApplicationId is required to create a command reply endpoint.");

        if (request.RequestId == Guid.Empty) request.RequestId = command.MessageId;
        if (string.IsNullOrWhiteSpace(request.ReplyTo)) request.ReplyTo = applicationId;
    }
}
