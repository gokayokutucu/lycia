// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Initializes transport-independent request/reply metadata.</summary>
public static class RequestRouting
{
    /// <summary>
    /// Ensures a command has a request identifier and a canonical logical response endpoint.
    /// </summary>
    public static void Prepare(ICommand command, string? responseEndpoint = null)
    {
        if (!(command is IRequestRoutingMetadata request))
            throw new InvalidOperationException(
                $"Command '{command.GetType().FullName}' must implement IRequestRoutingMetadata. Derive it from CommandBase.");

        if (command.MessageId == Guid.Empty)
            throw new InvalidOperationException($"Command '{command.GetType().FullName}' must have a MessageId before sending.");

        request.RequestId = command.MessageId;
        var endpoint = string.IsNullOrWhiteSpace(request.ResponseEndpoint)
            ? responseEndpoint ?? CommandEndpointResolver.Default.Resolve(command.GetType())
            : request.ResponseEndpoint!;
        request.ResponseEndpoint = EndpointIdentityNormalizer.Default.Normalize(endpoint);
    }
}
