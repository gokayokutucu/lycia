namespace Lycia.Common.Messaging;

#if NET8_0_OR_GREATER
public readonly record struct IncomingMessage(
byte[] Body,
Type MessageType,
Type HandlerType,
IReadOnlyDictionary<string, object?> Headers,
Func<ValueTask> Ack,
Func<bool, ValueTask> Nack
)
{
    public byte[] Body { get; } = Body;
    public Type MessageType { get; } = MessageType;
    public Type HandlerType { get; } = HandlerType;
    public IReadOnlyDictionary<string, object?> Headers { get; } = Headers;
    public Func<ValueTask> Ack { get; } = Ack;
    public Func<bool, ValueTask> Nack { get; } = Nack;
} 
#endif