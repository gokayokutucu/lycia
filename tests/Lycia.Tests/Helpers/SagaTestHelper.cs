// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga;

namespace Lycia.Tests.Helpers;

public static class SagaTestHelper
{
    public static Guid? GetMessageId<TMessage, THandler>(IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata> steps)
        where TMessage : class
        where THandler : class
    {
        var stepEntry = steps.FirstOrDefault(x =>
        {
            var stepTypeFullName = typeof(TMessage).FullName;
            var handlerTypeFullName = typeof(THandler).FullName;
            return stepTypeFullName != null &&
                   x.Key.stepType.Contains(stepTypeFullName) &&
                   x.Key.handlerType.Contains(handlerTypeFullName);
        });

        return string.IsNullOrWhiteSpace(stepEntry.Key.messageId) ? Guid.Empty : Guid.Parse(stepEntry.Key.messageId);
    }
}