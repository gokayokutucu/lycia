using System.Text.Json;
using Lycia.Messaging;
using Lycia.Saga;

namespace Lycia.Tests.Helper;

public static class SagaDispatcherTestHelper
{
    public static Guid? GetMessageId<TMessage, THandler>(IReadOnlyDictionary<(string stepType, string handlerType), SagaStepMetadata> steps)
        where TMessage : class
        where THandler : class
    {
        var stepEntry = steps.FirstOrDefault(x =>
            x.Key.stepType == typeof(TMessage).FullName &&
            x.Key.handlerType == typeof(THandler).FullName);

        if (stepEntry.Value is null) return Guid.Empty;
        var payload = JsonSerializer.Deserialize<TMessage>(stepEntry.Value.MessagePayload);
        return (payload as IMessage)?.MessageId;
    }
}