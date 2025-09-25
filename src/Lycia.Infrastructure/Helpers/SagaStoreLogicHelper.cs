// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging.Enums;
using Lycia;
using Lycia.Helpers;

namespace Lycia.Infrastructure.Helpers;

public static class SagaStoreLogicHelper
{
    public static string GetMessageTypeName(Type stepType)
    {
        return stepType.AssemblyQualifiedName 
               ?? throw new InvalidOperationException($"Step type {stepType.FullName} does not have an AssemblyQualifiedName");
    }

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