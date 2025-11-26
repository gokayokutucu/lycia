namespace Lycia.Common.Messaging;

#if NETSTANDARD2_0
using System;
using System.Collections.Generic;

public readonly record struct IncomingMessage
{
    public byte[] Body { get; }
    public Type MessageType { get; }
    public Type HandlerType { get; }
    public IReadOnlyDictionary<string, object> Headers { get; }
    public Action Ack { get; }
    public Action<bool> Nack { get; }

    public IncomingMessage(
        byte[] body,
        Type messageType,
        Type handlerType,
        IReadOnlyDictionary<string, object> headers,
        Action ack,
        Action<bool> nack)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
        Headers = headers ?? new Dictionary<string, object>();
        Ack = ack ?? (() => { });
        Nack = nack ?? (_ => { });
    }
}
#endif
