using Avro.Specific;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Lycia.Extensions.Converters;

/// <summary>
/// A custom contract resolver that extends CamelCasePropertyNamesContractResolver
/// to provide additional compatibility with Avro objects implementing ISpecificRecord.
/// Specifically, it ensures that the "Schema" property in Avro objects is ignored
/// during serialization and deserialization processes.
/// </summary>
public sealed class AvroFriendlyContractResolver : CamelCasePropertyNamesContractResolver
{
    /// <summary>
    /// Creates a list of JSON properties for the given type, with custom handling
    /// for types implementing the <see cref="ISpecificRecord"/> interface.
    /// Specifically, it ensures that the "Schema" property in Avro objects is ignored
    /// during both serialization and deserialization.
    /// </summary>
    /// <param name="type">The type of object for which to create JSON properties.</param>
    /// <param name="memberSerialization">The member serialization mode for the type.</param>
    /// <returns>
    /// A list of <see cref="JsonProperty"/> objects for the given type,
    /// with modifications applied for compatibility with Avro objects where applicable.
    /// </returns>
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