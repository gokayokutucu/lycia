namespace Lycia.Extensions.Nats;

/// <summary>Configuration for the Lycia NATS transport.</summary>
public sealed class NatsEventBusOptions
{
    /// <summary>Gets or sets the NATS server URL.</summary>
    public string Url { get; set; } = "nats://localhost:4222";
    /// <summary>Gets or sets the logical application identity shared by all replicas.</summary>
    public string ApplicationId { get; set; } = null!;
    /// <summary>Gets or sets whether durable JetStream delivery is used instead of ephemeral Core NATS.</summary>
    public bool UseJetStream { get; set; } = true;
    /// <summary>Gets or sets the JetStream stream containing Lycia subjects.</summary>
    public string StreamName { get; set; } = "LYCIA_MESSAGES";
    /// <summary>Gets or sets the time allowed before an unacknowledged delivery is eligible for redelivery.</summary>
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the maximum JetStream delivery attempts.</summary>
    public long MaxDeliver { get; set; } = 5;
    /// <summary>Gets or sets native scheduling behavior. The supported NATS 2.11 baseline falls back to SchedulerWorker.</summary>
    public NatsSchedulingMode SchedulingMode { get; set; } = NatsSchedulingMode.FallbackToWorker;
}

/// <summary>NATS delayed-delivery capability policy.</summary>
public enum NatsSchedulingMode
{
    /// <summary>Always use the durable SchedulerWorker.</summary>
    Disabled,
    /// <summary>Use native delayed delivery only after capability validation; otherwise use SchedulerWorker.</summary>
    FallbackToWorker,
    /// <summary>Require native delayed delivery and fail clearly when the server/client baseline cannot provide it.</summary>
    NativeOnly
}
