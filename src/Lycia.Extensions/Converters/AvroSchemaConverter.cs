using Avro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lycia.Extensions.Converters;

public sealed class AvroSchemaConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
        => typeof(Schema).IsAssignableFrom(objectType);

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

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        var json = value.ToString(); // Avro Schema JSON
        try { writer.WriteRawValue(json); }
        catch { JToken.Parse(json).WriteTo(writer); }
    }

}