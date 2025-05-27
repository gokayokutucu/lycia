using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Messaging;

public class CommandBase: ICommand
{
    public Guid MessageId { get; init; } = Guid.NewGuid(); // Changed to NewGuid()
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ApplicationId { get; init; } = EventMetadata.ApplicationId;
#if UNIT_TESTING
    public StepStatus? __TestStepStatus { get; set; }
    public string? __TestStepType { get; set; }
#endif
}