using System.Text.Json;
using Lycia.Messaging;
using Lycia.Saga;

namespace Lycia.Tests.Helper;

public static class SagaDispatcherTestHelper
{
    public static Guid? GetMessageId<TMessage, THandler>(IReadOnlyDictionary<(string stepType, string handlerType, string messageId), SagaStepMetadata> steps)
        where TMessage : class
        where THandler : class
    {
        var stepEntry = steps.FirstOrDefault(x =>
        {
            var fullName = typeof(TMessage).FullName;
            return fullName != null &&
                   x.Key.stepType.Contains(fullName) &&
                   x.Key.handlerType == typeof(THandler).FullName;
        });

        return string.IsNullOrWhiteSpace(stepEntry.Key.messageId) ? Guid.Empty : Guid.Parse(stepEntry.Key.messageId);
    }
}