// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using System.Text;
using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Produces transport-safe canonical logical application keys.</summary>
public sealed class EndpointIdentityNormalizer : IEndpointIdentityNormalizer
{
    /// <summary>Gets the shared stateless normalizer.</summary>
    public static EndpointIdentityNormalizer Default { get; } = new();

    /// <inheritdoc />
    public string Normalize(string endpointIdentity)
    {
        if (endpointIdentity == null) throw new ArgumentNullException(nameof(endpointIdentity));

        var canonical = new StringBuilder(endpointIdentity.Length);
        foreach (var character in endpointIdentity)
        {
            if (IsSeparator(character)) continue;
            if (!IsAsciiAlphaNumeric(character))
                throw new ArgumentException(
                    $"Endpoint identity '{endpointIdentity}' contains unsupported character '{character}'. " +
                    "Use ASCII letters, digits, dash, underscore, dot, or whitespace.",
                    nameof(endpointIdentity));

            canonical.Append(char.ToLowerInvariant(character));
        }

        if (canonical.Length == 0)
            throw new ArgumentException(
                $"Endpoint identity '{endpointIdentity}' must contain at least one ASCII letter or digit.",
                nameof(endpointIdentity));

        return canonical.ToString();
    }

    private static bool IsSeparator(char character) =>
        character == '-' || character == '_' || character == '.' || char.IsWhiteSpace(character);

    private static bool IsAsciiAlphaNumeric(char character) =>
        character >= 'a' && character <= 'z' ||
        character >= 'A' && character <= 'Z' ||
        character >= '0' && character <= '9';
}
