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
}
