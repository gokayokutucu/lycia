// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Helpers;

/// <summary>Provides shared type-name and step-key logic for saga-store implementations.</summary>
public static class SagaStoreLogicHelper
{
    /// <summary>Returns the assembly-qualified message type name stored with a saga step.</summary>
    public static string GetMessageTypeName(Type stepType)
    {
        return stepType.AssemblyQualifiedName 
               ?? throw new InvalidOperationException($"Step type {stepType.FullName} does not have an AssemblyQualifiedName");
    }

    /// <summary>Parses a persisted step key into step type, handler type, and message identifier components.</summary>
    public static (string stepType, string handlerType, string messageId) ParseStepKey(string key)
    {
        // Parse Redis/in-memory key and return tuple or null for malformed
        // Expected format: step:{stepType}:assembly:{assembly}:handler:{handlerType}:assembly:{assembly}:message:{messageId}
        var parts = key.Split(':');
        if (parts.Length == 10 &&
            parts[0] == "step" &&
            parts[2] == "assembly" &&
            parts[4] == "handler" &&
            parts[6] == "assembly" &&
            parts[8] == "message-id")
        {
            var stepTypeName = $"{parts[1]}, {parts[3]}";
            var handlerTypeName = $"{parts[5]}, {parts[7]}";
            var messageId = parts[9];
            return (stepTypeName, handlerTypeName, messageId);
        }
        return (key, string.Empty, Guid.Empty.ToString());
    }
}
