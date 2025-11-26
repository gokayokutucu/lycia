using System.Text;
using Avro;
using Avro.IO;
using Avro.Specific;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Saga.Contexts;

namespace Lycia.Extensions.Serialization;

public sealed class AvroMessageSerializer : IMessageSerializer
{
    private const string H_SchemaId = "lycia-schema-id";
    private const string H_Type = "lycia-type";
    private const string H_Content = "content-type";
    private const string H_Version = "lycia-schema-ver";

    public string ContentTypeHeaderKey => H_Content;

    public (byte[] Body, IReadOnlyDictionary<string, object?> Headers) Serialize(
        object message,
        MessageSerializationContext ctx)
    {
        if (message is not ISpecificRecord specific)
            throw new InvalidOperationException($"AvroMessageSerializer requires ISpecificRecord, got {message.GetType().FullName}.");

        var schema = specific.Schema;
        using var ms = new MemoryStream();
        var encoder = new BinaryEncoder(ms);
        var writer = new SpecificDatumWriter<object>(schema);
        SanitizeForSchema(message, schema);
        writer.Write(message, encoder);
        var bytes = ms.ToArray();

        var typeName = ctx.ExplicitTypeName ?? message.GetType().AssemblyQualifiedName!;
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [H_Content] = "avro/binary",
            [H_Type] = typeName,
            [H_SchemaId] = schema.Fullname,
            [H_Version] = null
        };

        return (bytes, headers);
    }

    public object Deserialize(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        MessageSerializationContext ctx)
    {
        var typeName = GetRequiredHeaderString(headers, H_Type, "Missing lycia-type header.");
        var targetType = Type.GetType(typeName, throwOnError: true)!;

        if (Activator.CreateInstance(targetType) is not ISpecificRecord proto)
            throw new InvalidOperationException($"Type {targetType.FullName} must implement ISpecificRecord for Avro deserialization.");

        var schema = proto.Schema;
        using var ms = new MemoryStream(body.ToArray());
        var decoder = new BinaryDecoder(ms);
        var reader = new SpecificDatumReader<object>(schema, schema);
        var obj = reader.Read(default, decoder);

        return obj ?? throw new InvalidOperationException($"Avro deserialization returned null for {typeName}.");
    }

    public IReadOnlyDictionary<string, object?> CreateHeadersForStoredPayload(
        Type payloadClrType,
        string? schemaId = null,
        string? schemaVersion = null)
    {
        string? resolvedSchemaId = schemaId;
        if (resolvedSchemaId == null && typeof(ISpecificRecord).IsAssignableFrom(payloadClrType))
        {
            if (Activator.CreateInstance(payloadClrType) is ISpecificRecord proto)
                resolvedSchemaId = proto.Schema.Fullname;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [H_Content] = "avro/binary",
            [H_Type] = payloadClrType.AssemblyQualifiedName!,
            [H_SchemaId] = resolvedSchemaId,
            [H_Version] = schemaVersion
        };
    }

    public IReadOnlyDictionary<string, object?> NormalizeTransportHeaders(IReadOnlyDictionary<string, object?> incomingHeaders)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in incomingHeaders)
            normalized[kvp.Key] = kvp.Value;

        if (!normalized.ContainsKey(H_Content))
        {
            foreach (var key in incomingHeaders.Keys)
                if (string.Equals(key, H_Content, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[H_Content] = incomingHeaders[key];
                    break;
                }
        }

        if (!normalized.ContainsKey(H_Type))
        {
            foreach (var key in incomingHeaders.Keys)
                if (string.Equals(key, H_Type, StringComparison.OrdinalIgnoreCase))
                {
                    normalized[H_Type] = incomingHeaders[key];
                    break;
                }
        }

        if (normalized.TryGetValue(H_Content, out var c))
            normalized[H_Content] = AsString(c);
        if (normalized.TryGetValue(H_Type, out var t))
            normalized[H_Type] = AsString(t);

        return normalized;
    }

    public (IReadOnlyDictionary<string, object?> Headers, MessageSerializationContext Ctx)
        CreateContextFor(Type payloadType, string? schemaId = null, string? schemaVersion = null)
    {
        string? resolvedSchemaId = schemaId;
        if (resolvedSchemaId == null && typeof(ISpecificRecord).IsAssignableFrom(payloadType))
        {
            if (Activator.CreateInstance(payloadType) is ISpecificRecord proto)
                resolvedSchemaId = proto.Schema.Fullname;
        }

        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [H_Content] = "avro/binary",
            [H_Type] = payloadType.AssemblyQualifiedName!,
            [H_SchemaId] = resolvedSchemaId,
            [H_Version] = schemaVersion
        };

        var ctx = new MessageSerializationContext
        {
            ExplicitTypeName = payloadType.AssemblyQualifiedName
        };

        return (headers, ctx);
    }

    private static string? AsString(object? value)
    {
        switch (value)
        {
            case null: return null;
            case string s: return s;
            case byte[] bytes: return Encoding.UTF8.GetString(bytes);
            case ReadOnlyMemory<byte> rom: return Encoding.UTF8.GetString(rom.ToArray());
            default: return value.ToString();
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



    static void SanitizeForSchema(object? value, Schema schema)
    {
        if (value == null) return;

        switch (schema)
        {
            case UnionSchema us:
                {
                    // null dışındaki dal(lar)ı sanitize et
                    foreach (var b in us.Schemas.Where(x => x.Tag != Schema.Type.Null))
                        SanitizeForSchema(value, b);
                    break;
                }

            case RecordSchema rs:
                {
                    if (value is ISpecificRecord sr)
                    {
                        foreach (var f in rs.Fields)
                        {
                            var fv = sr.Get(f.Pos);
                            if (fv == null)
                            {
                                if (IsStringSchema(f.Schema)) { sr.Put(f.Pos, string.Empty); continue; }
                                if (TryArrayClr(f.Schema, out var itemClr))
                                {
                                    var listType = typeof(List<>).MakeGenericType(itemClr ?? typeof(string));
                                    sr.Put(f.Pos, Activator.CreateInstance(listType));
                                    continue;
                                }
                            }
                            else
                            {
                                SanitizeForSchema(fv, f.Schema);
                            }
                        }
                    }
                    else
                    {
                        var type = value.GetType();
                        foreach (var f in rs.Fields)
                        {
                            var prop = type.GetProperty(f.Name);
                            if (prop == null || !prop.CanRead || !prop.CanWrite) continue;

                            var fv = prop.GetValue(value);
                            if (fv == null)
                            {
                                if (IsStringSchema(f.Schema)) { prop.SetValue(value, ""); continue; }
                                if (TryArrayClr(f.Schema, out var itemClr))
                                {
                                    var listType = typeof(List<>).MakeGenericType(itemClr ?? typeof(string));
                                    prop.SetValue(value, Activator.CreateInstance(listType));
                                    continue;
                                }
                            }
                            else
                            {
                                SanitizeForSchema(fv, f.Schema);
                            }
                        }
                    }
                    break;
                }

            case ArraySchema arr:
                {
                    if (value is System.Collections.IEnumerable en)
                        foreach (var item in en) SanitizeForSchema(item!, arr.ItemSchema);
                    break;
                }

                // primitive’lerde işlem yok
        }
    }

    static bool IsStringSchema(Schema s)
    {
        if (s.Tag == Schema.Type.String) return true;
        if (s is UnionSchema u) return u.Schemas.Any(x => x.Tag == Schema.Type.String);
        return false;
    }

    static bool TryArrayClr(Schema s, out Type? itemClr)
    {
        itemClr = null;
        ArraySchema? arr = s as ArraySchema;
        if (arr == null && s is UnionSchema u) arr = u.Schemas.OfType<ArraySchema>().FirstOrDefault();
        if (arr == null) return false;

        itemClr =
            arr.ItemSchema.Tag == Schema.Type.String ? typeof(string) :
            arr.ItemSchema.Tag == Schema.Type.Int ? typeof(int) :
            arr.ItemSchema.Tag == Schema.Type.Double ? typeof(double) : typeof(object);
        return true;
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
        return CreateContextFor(payloadType, schemaId, schemaVersion);
    }
}