namespace Lycia.Messaging;

public abstract class SagaData
{
    public Guid SagaId { get; set; }
    public bool IsCompleted { get; set; }
    public Type? FailedStepType { get; set; }
    public Type? FailedHandlerType { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
}