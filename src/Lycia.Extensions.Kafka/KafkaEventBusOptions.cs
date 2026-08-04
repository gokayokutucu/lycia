namespace Lycia.Extensions.Kafka;

/// <summary>Configuration for the Lycia Kafka transport.</summary>
public sealed class KafkaEventBusOptions
{
    /// <summary>Gets or sets the Kafka bootstrap server list.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";
    /// <summary>Gets or sets the logical application identity shared by all replicas.</summary>
    public string ApplicationId { get; set; } = null!;
    /// <summary>Gets or sets the prefix applied to every Lycia topic and consumer group.</summary>
    public string TopicPrefix { get; set; } = "lycia";
    /// <summary>Gets or sets the partition count used when Lycia creates topics.</summary>
    public int NumPartitions { get; set; } = 3;
    /// <summary>Gets or sets the replication factor used when Lycia creates topics.</summary>
    public short ReplicationFactor { get; set; } = 1;
    /// <summary>Gets or sets whether the transport creates missing topics.</summary>
    public bool EnsureTopics { get; set; } = true;
    /// <summary>Gets or sets where a group begins when it has no committed offset.</summary>
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;
}

/// <summary>Offset position used when a Kafka consumer group has no committed offset.</summary>
public enum AutoOffsetReset
{
    /// <summary>Begin with the earliest retained record.</summary>
    Earliest,
    /// <summary>Begin after the latest retained record.</summary>
    Latest
}
