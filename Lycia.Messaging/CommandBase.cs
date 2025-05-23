using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public class CommandBase: ICommand
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
}