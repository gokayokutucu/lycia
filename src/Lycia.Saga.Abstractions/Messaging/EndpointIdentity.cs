// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Text;

namespace Lycia.Saga.Abstractions.Messaging;

/// <summary>Canonical logical endpoint identity rules shared by messaging and transports.</summary>
public static class EndpointIdentity
{
    /// <summary>Removes supported separators and lowercases an ASCII endpoint identity.</summary>
    public static string Normalize(string endpointIdentity)
    {
        if (endpointIdentity == null) throw new ArgumentNullException(nameof(endpointIdentity));

        var canonical = new StringBuilder(endpointIdentity.Length);
        foreach (var character in endpointIdentity)
        {
            if (character == '-' || character == '_' || character == '.' || char.IsWhiteSpace(character))
                continue;
            if (!(character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' ||
                  character >= '0' && character <= '9'))
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
}
