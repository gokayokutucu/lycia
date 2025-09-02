namespace Lycia.Extensions.Serialization;

public sealed class MessageSerializationContext
{
    public string ApplicationId { get; set; } = "";
    public string? ExplicitTypeName { get; set; }
}