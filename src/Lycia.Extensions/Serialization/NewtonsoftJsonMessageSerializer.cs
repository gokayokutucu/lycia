// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text;
using Lycia.Extensions.Converters;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lycia.Extensions.Serialization;

/// <summary>
/// A serializer implementation that uses Newtonsoft.Json to serialize and deserialize message payloads.
/// </summary>
public sealed class NewtonsoftJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        //ContractResolver = new CamelCasePropertyNamesContractResolver(),
        ContractResolver = new AvroFriendlyContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters = { new StringEnumConverter(), new AvroSchemaConverter() },
        TypeNameHandling = TypeNameHandling.None
    };

    private const string HeaderSchemaId = "lycia-schema-id";
    private const string HeaderType = "lycia-type"; // "Namespace.Type, Assembly"
    private const string HeaderContent = "content-type"; // "application/json"
    private const string HeaderVersion = "lycia-schema-ver";

    /// <summary>
    /// Gets the header key used to identify the content type in message serialization.
    /// This value typically indicates the content type, such as "application/json",
    /// and is utilized during message serialization and deserialization processes.
    /// </summary>
    public string ContentTypeHeaderKey  => HeaderContent;

    /// <summary>
    /// Serializes a given message into a JSON payload along with its associated headers.
    /// </summary>
    /// <param name="message">The object to be serialized.</param>
    /// <param name="ctx">The serialization context containing metadata such as explicit type name.</param>
    /// <returns>A tuple containing the serialized JSON payload as a byte array and a read-only dictionary of headers.</returns>
    public (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(
        object message,
        IMessageSerializationContext ctx)
    {
        var typeName = ctx.ExplicitTypeName ?? message.GetType().AssemblyQualifiedName!;
        var json = JsonConvert.SerializeObject(message, Settings);
        var bytes = Encoding.UTF8.GetBytes(json);

        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderContent] = "application/json",
            [HeaderType] = typeName,
            [HeaderSchemaId] = null, // It is for avro/protobuf, not used here
            [HeaderVersion] = null
        };

        return (bytes, headers);
    }

    private static string? AsString(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case byte[] bytes:
                return Encoding.UTF8.GetString(bytes);
            case ReadOnlyMemory<byte> rom:
                return Encoding.UTF8.GetString(rom.ToArray());
            default:
                return value.ToString();
        }
    }

    private static string GetRequiredHeaderString(
        IReadOnlyDictionary<string, object?> headers,
        string key,
        string errorMessage)
    {
        if (headers.TryGetValue(key, out var raw))
        {
            var s = AsString(raw);
            if (!string.IsNullOrWhiteSpace(s))
                return s!;
        }
        throw new InvalidOperationException(errorMessage);
    }


    /// <summary>
    /// Deserializes a given JSON-encoded message body into an object of the specified type.
    /// </summary>
    /// <param name="body">The serialized message as a read-only byte array.</param>
    /// <param name="headers">A read-only dictionary of transport headers associated with the message.</param>
    /// <param name="ctx">The deserialization context providing additional metadata.</param>
    /// <returns>The deserialized object of the type specified in the message headers.</returns>
    /// <exception cref="JsonSerializationException">Thrown if deserialization fails or returns null.</exception>
    public object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        IMessageSerializationContext ctx)
    {
        var typeName = GetRequiredHeaderString(headers, HeaderType, "Missing lycia-type header.");
        var targetType = Type.GetType(typeName, throwOnError: true)!;

        var json = Encoding.UTF8.GetString(body.ToArray());
        var obj = JsonConvert.DeserializeObject(json, targetType, Settings);
        if (obj is null)
            throw new JsonSerializationException($"Deserialization returned null for {typeName}.");
        return obj;
    }

    /// <summary>
    /// Creates a case-insensitive dictionary of headers for a stored payload, including content type, type, schema id, and version.
    /// </summary>
    /// <param name="payloadClrType">The CLR type of the payload.</param>
    /// <param name="schemaId">Optional schema identifier.</param>
    /// <param name="schemaVersion">Optional schema version.</param>
    /// <returns>A case-insensitive dictionary of headers.</returns>
    public IReadOnlyDictionary<string, object?> CreateHeadersForStoredPayload(Type payloadClrType, string? schemaId = null,
        string? schemaVersion = null)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderContent]  = "application/json",
            [HeaderType]     = payloadClrType.AssemblyQualifiedName!,
            [HeaderSchemaId] = schemaId,
            [HeaderVersion]  = schemaVersion
        };
    }

    /// <summary>
    /// Normalizes transport headers by copying all entries into a case-insensitive dictionary.
    /// Ensures that standard headers are present if equivalent keys exist in different casing.
    /// </summary>
    /// <param name="incomingHeaders">The incoming headers to normalize.</param>
    /// <returns>A case-insensitive dictionary of normalized headers.</returns>
    public IReadOnlyDictionary<string, object?> NormalizeTransportHeaders(IReadOnlyDictionary<string, object?> incomingHeaders)
    {
        // Copy all entries into a case-insensitive dictionary
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in incomingHeaders)
        {
            normalized[kvp.Key] = kvp.Value;
        }

        // If H_Content is missing but a key like "content-type" (any casing) exists, ensure it's present
        if (!normalized.ContainsKey(HeaderContent))
        {
            foreach (var key in incomingHeaders.Keys)
            {
                if (string.Equals(key, HeaderContent, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[HeaderContent] = incomingHeaders[key];
                    break;
                }
            }
        }

        // If H_Type is missing but a key like "lycia-type" (any casing) exists, ensure it's present
        if (!normalized.ContainsKey(HeaderType))
        {
            foreach (var key in incomingHeaders.Keys)
            {
                if (string.Equals(key, HeaderType, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[HeaderType] = incomingHeaders[key];
                    break;
                }
            }
        }

        if (normalized.TryGetValue(HeaderContent, out var c))
            normalized[HeaderContent] = AsString(c);
        if (normalized.TryGetValue(HeaderType, out var t))
            normalized[HeaderType] = AsString(t);

        return normalized;
    }

    /// <summary>
    /// Creates a serialization context and headers for the specified payload type, schema ID, and schema version.
    /// </summary>
    /// <param name="payloadType">The type of the message payload to be serialized.</param>
    /// <param name="schemaId">An optional schema ID associated with the payload.</param>
    /// <param name="schemaVersion">An optional schema version associated with the payload.</param>
    /// <returns>A tuple containing a read-only dictionary of headers and the serialization context.</returns>
    public (IReadOnlyDictionary<string, object?> Headers, IMessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderContent]  = "application/json",
            [HeaderType]     = payloadType.AssemblyQualifiedName!,
            [HeaderSchemaId] = schemaId,
            [HeaderVersion]  = schemaVersion
        };

        var ctx = new MessageSerializationContext
        {
            ExplicitTypeName = payloadType.AssemblyQualifiedName
        };

        return (headers, ctx);
    }
}