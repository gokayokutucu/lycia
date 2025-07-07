namespace Lycia.Messaging.Extensions;

public static class MessageExtensions
{
    public static TOut Next<TIn, TOut>(this TIn previous)
        where TIn : IMessage
        where TOut : IMessage, new()
    {
        var next = new TOut
        {
            CorrelationId = previous.CorrelationId,
        };
        return next;
    }
}