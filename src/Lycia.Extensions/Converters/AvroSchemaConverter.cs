using Avro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lycia.Extensions.Converters;

/// <summary>Serializes and deserializes Apache Avro <see cref="Schema"/> values as their JSON representation.</summary>
public sealed class AvroSchemaConverter : JsonConverter
{
    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
        => typeof(Schema).IsAssignableFrom(objectType);

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        try
        {
            var token = JToken.Load(reader);

            if (token.Type == JTokenType.String)
                return Schema.Parse(token.Value<string>());

            return Schema.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex)
        {
            throw new JsonSerializationException("Cannot be parsed", ex);
        }
    }

    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        var json = value.ToString(); // Avro Schema JSON
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonSerializationException("The Avro schema did not produce a JSON representation.");
        try { writer.WriteRawValue(json); }
        catch { JToken.Parse(json).WriteTo(writer); }
    }

}
