using Lycia.Messaging;

namespace Lycia.Saga.Extensions;

public static class SagaMessageExtensions
{
    public static void SetSagaId(this IMessage message, Guid sagaId)
    {
        var prop = message.GetType().GetProperty("SagaId");
        if (prop != null && prop.CanWrite)
            prop.SetValue(message, sagaId);
    }
}