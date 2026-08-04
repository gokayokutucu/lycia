// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>
/// Resolves command ownership from an application-defined interface such as
/// <c>IStockServiceCommand</c>. Endpoint names are the marker name without its
/// leading <c>I</c> and trailing <c>Command</c> suffix.
/// </summary>
public sealed class CommandEndpointResolver : ICommandEndpointResolver
{
    /// <summary>Gets the shared stateless resolver instance.</summary>
    public static CommandEndpointResolver Default { get; } = new();

    /// <inheritdoc />
    public string Resolve(Type commandType)
    {
        if (commandType == null) throw new ArgumentNullException(nameof(commandType));
        if (!typeof(ICommand).IsAssignableFrom(commandType))
            throw new ArgumentException($"Type '{commandType.FullName}' does not implement ICommand.", nameof(commandType));

        var markers = commandType.GetInterfaces()
            .Where(IsApplicationEndpoint)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        if (markers.Length == 0)
            throw new InvalidOperationException(
                $"Command '{commandType.FullName}' must implement exactly one application command endpoint interface that inherits ICommandEndpoint.");

        if (markers.Length > 1)
            throw new InvalidOperationException(
                $"Command '{commandType.FullName}' implements multiple command endpoint interfaces: {string.Join(", ", markers.Select(type => type.FullName))}.");

        return NormalizeMarkerName(markers[0]);
    }

    internal static string NormalizeMarkerName(Type markerType)
    {
        var name = markerType.Name;
        if (!markerType.IsInterface || name.Length <= 8 || name[0] != 'I' ||
            !name.EndsWith("Command", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Command endpoint marker '{markerType.FullName}' must be an interface named I{{LogicalOwner}}Command.");
        }

        var endpoint = name.Substring(1, name.Length - "I".Length - "Command".Length);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException($"Command endpoint marker '{markerType.FullName}' has an empty logical owner name.");

        return endpoint;
    }

    private static bool IsApplicationEndpoint(Type type) =>
        type != typeof(ICommandEndpoint) && typeof(ICommandEndpoint).IsAssignableFrom(type);
}
