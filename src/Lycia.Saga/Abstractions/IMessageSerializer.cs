using Lycia.Extensions.Serialization;

namespace Lycia.Saga.Abstractions;

public interface IMessageSerializer
{
    string ContentTypeHeaderKey { get; }  
    
    (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(
        object message,
        MessageSerializationContext ctx);

    object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        MessageSerializationContext ctx);
    
    IReadOnlyDictionary<string, object?> CreateHeadersForStoredPayload(
        Type payloadClrType, string? schemaId = null, string? schemaVersion = null);

    IReadOnlyDictionary<string, object?> NormalizeTransportHeaders(
        IReadOnlyDictionary<string, object?> incomingHeaders);

    (IReadOnlyDictionary<string, object?> Headers, MessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null);
}