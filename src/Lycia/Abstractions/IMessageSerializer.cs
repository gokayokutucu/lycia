// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Serialization;

namespace Lycia.Abstractions;

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