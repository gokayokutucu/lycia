using System;
using System.Collections.Generic;
using System.Text;
using Avro.Generic;
using Avro.Specific;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;

namespace Lycia.Extensions.Serialization;
public sealed class CompositeMessageSerializer : IMessageSerializer
{
    private readonly IMessageSerializer _json;
    private readonly IMessageSerializer _avro;

    public CompositeMessageSerializer(
        NewtonsoftJsonMessageSerializer json,
        AvroMessageSerializer avro)
    {
        _json = json ?? throw new ArgumentNullException(nameof(json));
        _avro = avro ?? throw new ArgumentNullException(nameof(avro));
    }

    public string ContentTypeHeaderKey => _json.ContentTypeHeaderKey; // "content-type"

    private static bool LooksLikeJson(byte[] body)
    {
        if (body == null || body.Length == 0) return false;
        int i = 0;
        // whitespace atla
        while (i < body.Length)
        {
            var c = (char)body[i];
            if (!char.IsWhiteSpace(c)) break;
            i++;
        }
        if (i >= body.Length) return false;
        var ch = (char)body[i];
        return ch == '{' || ch == '[';
    }

    private static bool IsAvroType(Type t)
    {
        return typeof(ISpecificRecord).IsAssignableFrom(t) ||
               typeof(GenericRecord).IsAssignableFrom(t);
    }

    private static bool IsAvroObject(object o)
    {
        return o is ISpecificRecord || o is GenericRecord;
    }

    private IMessageSerializer PickForSerialize(object message, MessageSerializationContext ctx)
    {
        // headers yoksa bile tipten seçiyoruz
        if (message != null && IsAvroObject(message))
            return _avro;

        // (İstersen ctx.Headers ile zorlama eklersin; şimdi yok sayıyoruz)
        return _json;
    }

    public (byte[] Body, IReadOnlyDictionary<string, object?> Headers)
        Serialize(object message, MessageSerializationContext ctx)
    {
        return PickForSerialize(message, ctx).Serialize(message, ctx);
    }

    public object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        MessageSerializationContext ctx)
    {
        // header yoksa/faydasızsa: kokla + dene/olmazsa fallback
        var bytes = body.ToArray();

        Exception firstEx = null;

        if (LooksLikeJson(bytes))
        {
            try
            {
                return _json.Deserialize(bytes, headers ?? new Dictionary<string, object?>(), ctx);
            }
            catch (Exception exJson)
            {
                firstEx = exJson;
                // Avro’yu dene
                return TryAvroOrThrow(bytes, headers, ctx, firstEx);
            }
        }
        else
        {
            try
            {
                return _avro.Deserialize(bytes, headers ?? new Dictionary<string, object?>(), ctx);
            }
            catch (Exception exAvro)
            {
                firstEx = exAvro;
                // JSON’u dene
                try
                {
                    return _json.Deserialize(bytes, headers ?? new Dictionary<string, object?>(), ctx);
                }
                catch (Exception exJson)
                {
                    // İkisini birleştir
                    throw new AggregateException("Failed to deserialize as Avro then JSON.", firstEx, exJson);
                }
            }
        }
    }

    private object TryAvroOrThrow(byte[] bytes, IReadOnlyDictionary<string, object?> headers, MessageSerializationContext ctx, Exception firstEx)
    {
        try
        {
            return _avro.Deserialize(bytes, headers ?? new Dictionary<string, object?>(), ctx);
        }
        catch (Exception exAvro)
        {
            throw new AggregateException("Failed to deserialize as JSON then Avro.", firstEx, exAvro);
        }
    }

    public IReadOnlyDictionary<string, object?> CreateHeadersForStoredPayload(
        Type payloadClrType, string? schemaId = null, string? schemaVersion = null)
    {
        // headers yoksa da depoya yazarken tipten seçim yap
        var usesAvro = payloadClrType != null && IsAvroType(payloadClrType);
        return (usesAvro ? _avro : _json)
            .CreateHeadersForStoredPayload(payloadClrType, schemaId, schemaVersion);
    }

    public IReadOnlyDictionary<string, object?> NormalizeTransportHeaders(
        IReadOnlyDictionary<string, object?> incomingHeaders)
    {
        // case-insensitive normalize için JSON’unki iş görüyor
        return _json.NormalizeTransportHeaders(incomingHeaders ?? new Dictionary<string, object?>());
    }

    public (IReadOnlyDictionary<string, object?> Headers, IMessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null)
    {
        var usesAvro = payloadType != null && IsAvroType(payloadType);
        return (usesAvro ? _avro : _json).CreateContextFor(payloadType, schemaId, schemaVersion);
    }

    public (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(object message, IMessageSerializationContext ctx)
    {
        throw new NotImplementedException();//GOP
    }

    public object Deserialize(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, object?> headers, IMessageSerializationContext ctx)
    {
        throw new NotImplementedException();//GOP
    }

    (IReadOnlyDictionary<string, object?> Headers, IMessageSerializationContext Ctx) IMessageSerializer.CreateContextFor(Type payloadType, string? schemaId, string? schemaVersion)
    {
        throw new NotImplementedException();//GOP
    }
}
