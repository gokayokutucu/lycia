namespace Lycia.Saga.Abstractions.Contexts;

public class IMessageSerializationContext
{
    public string ApplicationId { get; set; }
    public string? ExplicitTypeName { get; set; }
}