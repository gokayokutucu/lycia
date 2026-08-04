// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Messaging;

/// <summary>Validates command ownership and handler uniqueness for one logical application.</summary>
public static class CommandTopologyValidator
{
    /// <summary>
    /// Validates endpoint ownership and one distinct handler type for every discovered command.
    /// Repeated registrations of the same handler type represent valid replicas.
    /// </summary>
    public static void Validate(
        string applicationId,
        IEnumerable<(Type MessageType, Type HandlerType)> registrations,
        ICommandEndpointResolver? endpointResolver = null)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ArgumentException("ApplicationId cannot be null or empty.", nameof(applicationId));
        if (registrations == null) throw new ArgumentNullException(nameof(registrations));

        var resolver = endpointResolver ?? CommandEndpointResolver.Default;
        var commands = registrations
            .Where(item => typeof(ICommand).IsAssignableFrom(item.MessageType))
            .GroupBy(item => item.MessageType)
            .OrderBy(group => group.Key.FullName, StringComparer.Ordinal);

        foreach (var command in commands)
            ValidateCommand(applicationId, command.Key, command.Select(item => item.HandlerType), resolver);
    }

    private static void ValidateCommand(
        string applicationId,
        Type commandType,
        IEnumerable<Type> handlerTypes,
        ICommandEndpointResolver resolver)
    {
        var handlers = handlerTypes.Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        var expectedOwner = resolver.Resolve(commandType);

        if (!string.Equals(expectedOwner, applicationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Command '{commandType.FullName}' is registered to handler(s) [{FormatHandlers(handlers)}] " +
                $"owned by '{expectedOwner}', but the actual ApplicationId is '{applicationId}'. " +
                "ApplicationId comparison is ordinal and case-insensitive.");
        }

        if (handlers.Length != 1)
        {
            throw new InvalidOperationException(
                $"Command '{commandType.FullName}' owned by '{expectedOwner}' must have exactly one handler type " +
                $"in ApplicationId '{applicationId}', but found {handlers.Length}: [{FormatHandlers(handlers)}].");
        }
    }

    private static string FormatHandlers(IEnumerable<Type> handlers) =>
        string.Join(", ", handlers.Select(type => type.FullName));
}
