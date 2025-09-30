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

    private const string H_SchemaId = "lycia-schema-id";
    private const string H_Type = "lycia-type"; // "Namespace.Type, Assembly"
    private const string H_Content = "content-type"; // "application/json"
    private const string H_Version = "lycia-schema-ver";

    public string ContentTypeHeaderKey  => H_Content;

    public (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(
        object message,
        IMessageSerializationContext ctx)
    {
        var typeName = ctx.ExplicitTypeName ?? message.GetType().AssemblyQualifiedName!;
        var json = JsonConvert.SerializeObject(message, Settings);
        var bytes = Encoding.UTF8.GetBytes(json);

        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [H_Content] = "application/json",
            [H_Type] = typeName,
            [H_SchemaId] = null, // It is for avro/protobuf, not used here
            [H_Version] = null
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



    public object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        IMessageSerializationContext ctx)
    {
        var typeName = GetRequiredHeaderString(headers, H_Type, "Missing lycia-type header.");
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
            [H_Content]  = "application/json",
            [H_Type]     = payloadClrType.AssemblyQualifiedName!,
            [H_SchemaId] = schemaId,
            [H_Version]  = schemaVersion
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
        if (!normalized.ContainsKey(H_Content))
        {
            foreach (var key in incomingHeaders.Keys)
            {
                if (string.Equals(key, H_Content, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[H_Content] = incomingHeaders[key];
                    break;
                }
            }
        }

        // If H_Type is missing but a key like "lycia-type" (any casing) exists, ensure it's present
        if (!normalized.ContainsKey(H_Type))
        {
            foreach (var key in incomingHeaders.Keys)
            {
                if (string.Equals(key, H_Type, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[H_Type] = incomingHeaders[key];
                    break;
                }
            }
        }

        if (normalized.TryGetValue(H_Content, out var c))
            normalized[H_Content] = AsString(c);
        if (normalized.TryGetValue(H_Type, out var t))
            normalized[H_Type] = AsString(t);

        return normalized;
    }
    
    public (IReadOnlyDictionary<string, object?> Headers, IMessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [H_Content]  = "application/json",
            [H_Type]     = payloadType.AssemblyQualifiedName!,
            [H_SchemaId] = schemaId,
            [H_Version]  = schemaVersion
        };

        var ctx = new MessageSerializationContext
        {
            ExplicitTypeName = payloadType.AssemblyQualifiedName
        };

        return (headers, ctx);
    }
}