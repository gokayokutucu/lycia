using Avro.Specific;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Lycia.Extensions.Converters;

/// <summary>Uses camel-case JSON names while excluding generated Avro schema metadata from record payloads.</summary>
public sealed class AvroFriendlyContractResolver : CamelCasePropertyNamesContractResolver
{
    /// <inheritdoc />
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        var props = base.CreateProperties(type, memberSerialization);

        if (!typeof(ISpecificRecord).IsAssignableFrom(type)) return props;
        
        foreach (var p in props.Where(p => string.Equals(p.PropertyName, "schema", StringComparison.OrdinalIgnoreCase)))
        {
            p.Ignored = true;
            p.ShouldSerialize = _ => false;
            p.ShouldDeserialize = _ => false;
        }

        return props;
    }
}
