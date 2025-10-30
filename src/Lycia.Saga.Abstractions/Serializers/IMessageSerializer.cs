// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Contexts;

namespace Lycia.Saga.Abstractions.Serializers;

public interface IMessageSerializer
{
    /// <summary>
    /// Represents a key used to define the header name for the content type in message serialization.
    /// </summary>
    /// <remarks>
    /// This property is utilized by implementations of <see cref="IMessageSerializer"/> to specify
    /// the key under which the content type metadata is stored in message headers.
    /// It allows message consumers to interpret the format of serialized messages appropriately.
    /// </remarks>
    string ContentTypeHeaderKey { get; }

    /// <summary>
    /// Serializes the given message into a byte array (body) and a dictionary of headers.
    /// </summary>
    /// <param name="message">The message object to be serialized.</param>
    /// <param name="ctx">The serialization context that provides additional metadata or configuration for the process.</param>
    /// <returns>
    /// A tuple containing a byte array representation of the serialized message
    /// and a read-only dictionary of headers associated with the serialization.
    /// </returns>
    (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(
        object message,
        IMessageSerializationContext ctx);

    /// <summary>
    /// Deserializes the provided byte array and headers into an object using the given serialization context.
    /// </summary>
    /// <param name="body">The serialized byte array representing the object to be deserialized.</param>
    /// <param name="headers">A read-only dictionary of headers that may contain metadata or additional information for deserialization.</param>
    /// <param name="ctx">The deserialization context that provides configuration or metadata needed for the process.</param>
    /// <returns>
    /// The deserialized object reconstructed from the provided byte array and headers.
    /// </returns>
    object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        IMessageSerializationContext ctx);

    /// <summary>
    /// Creates a case-insensitive dictionary of headers for a stored payload,
    /// including content type, payload type, schema identifier, and schema version.
    /// </summary>
    /// <param name="payloadClrType">The CLR type of the payload being stored.</param>
    /// <param name="schemaId">An optional schema identifier associated with the payload.</param>
    /// <param name="schemaVersion">An optional schema version linked to the payload.</param>
    /// <returns>
    /// A read-only dictionary containing the headers for the stored payload,
    /// with keys being case-insensitive.
    /// </returns>
    IReadOnlyDictionary<string, object?> CreateHeadersForStoredPayload(
        Type payloadClrType, string? schemaId = null, string? schemaVersion = null);

    /// <summary>
    /// Normalizes the transport headers by ensuring a case-insensitive mapping of keys and
    /// formatting specific required headers. If certain headers are missing but equivalent
    /// entries with different casing exist, those headers will be added or updated.
    /// </summary>
    /// <param name="incomingHeaders">The original set of transport headers to normalize.</param>
    /// <returns>
    /// A normalized dictionary containing the transport headers with consistent casing
    /// and formatting suitable for further processing.
    /// </returns>
    IReadOnlyDictionary<string, object?> NormalizeTransportHeaders(
        IReadOnlyDictionary<string, object?> incomingHeaders);

    /// <summary>
    /// Creates a serialization context and associated headers for a given payload type.
    /// </summary>
    /// <param name="payloadType">The type of the payload for which the serialization context is created.</param>
    /// <param name="schemaId">The optional schema identifier associated with the payload.</param>
    /// <param name="schemaVersion">The optional version of the schema used for the payload.</param>
    /// <returns>
    /// A tuple containing a read-only dictionary of headers related to the serialization
    /// and an instance of <see cref="IMessageSerializationContext"/> that provides metadata for serialization.
    /// </returns>
    (IReadOnlyDictionary<string, object?> Headers, IMessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null);
}