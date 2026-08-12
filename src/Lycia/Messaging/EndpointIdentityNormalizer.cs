// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Produces transport-safe canonical logical application keys.</summary>
public sealed class EndpointIdentityNormalizer : IEndpointIdentityNormalizer
{
    /// <summary>Gets the shared stateless normalizer.</summary>
    public static EndpointIdentityNormalizer Default { get; } = new();

    /// <inheritdoc />
    public string Normalize(string endpointIdentity) => EndpointIdentity.Normalize(endpointIdentity);
}
