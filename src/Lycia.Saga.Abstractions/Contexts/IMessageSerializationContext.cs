namespace Lycia.Saga.Abstractions.Contexts;

public class IMessageSerializationContext
{
    public string ApplicationId { get; set; } = string.Empty;
    public string? ExplicitTypeName { get; set; }
}
