namespace Lycia.Messaging.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ApplicationIdAttribute(string applicationId) : Attribute
{
    public string ApplicationId { get; } = applicationId;
}