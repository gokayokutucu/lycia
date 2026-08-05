// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Scheduling;

/// <summary>Lifecycle states persisted for a scheduling request.</summary>
public enum ScheduleStatus
{
    /// <summary>Waiting for its due time.</summary>
    Pending,
    /// <summary>Atomically claimed by a worker.</summary>
    Claimed,
    /// <summary>Being accepted by the final transport.</summary>
    Dispatching,
    /// <summary>Accepted by the final transport.</summary>
    Completed,
    /// <summary>Waiting for a retry after a transient failure.</summary>
    RetryPending,
    /// <summary>Permanently failed.</summary>
    Failed,
    /// <summary>Cancelled before dispatch.</summary>
    Cancelled
}

/// <summary>Transport-independent final delivery semantic.</summary>
public enum ScheduledMessageKind
{
    /// <summary>Point-to-point command delivery.</summary>
    Command,
    /// <summary>Broadcast event delivery.</summary>
    Event,
    /// <summary>Targeted response delivery.</summary>
    Response
}

/// <summary>Scheduling mechanism selected for a request.</summary>
public enum SchedulingStrategy
{
    /// <summary>Durable store and SchedulerWorker dispatch.</summary>
    DurableWorker,
    /// <summary>RabbitMQ fixed TTL and dead-letter routing.</summary>
    RabbitMqTtlDeadLetter,
    /// <summary>NATS server-native delayed delivery after capability validation.</summary>
    NatsNative
}

/// <summary>Durable representation of one idempotent scheduling request.</summary>
public sealed class ScheduleRecord
{
    /// <summary>Scheduling-operation idempotency key, distinct from <see cref="MessageId"/>.</summary>
    public Guid ScheduleId { get; set; }
    /// <summary>Identity of the scheduled message; unchanged during dispatch and retry.</summary>
    public Guid MessageId { get; set; }
    /// <summary>Original request identity when the message carries request-routing metadata.</summary>
    public Guid? RequestId { get; set; }
    /// <summary>Workflow correlation identity preserved across deferred delivery.</summary>
    public Guid CorrelationId { get; set; }
    /// <summary>Direct causal message identity used for tracing.</summary>
    public Guid? CausationId { get; set; }
    /// <summary>Compensation lineage parent identity.</summary>
    public Guid ParentMessageId { get; set; }
    /// <summary>Saga identity preserved across deferred delivery.</summary>
    public Guid? SagaId { get; set; }
    /// <summary>Canonical requester endpoint for command and targeted response routing.</summary>
    public string? ResponseEndpoint { get; set; }
    /// <summary>Assembly-qualified scheduled message type.</summary>
    public string MessageType { get; set; } = string.Empty;
    /// <summary>Final delivery semantic.</summary>
    public ScheduledMessageKind MessageKind { get; set; }
    /// <summary>Canonical logical destination.</summary>
    public string Destination { get; set; } = string.Empty;
    /// <summary>UTC instant after which dispatch may occur.</summary>
    public DateTimeOffset DueAtUtc { get; set; }
    /// <summary>UTC instant when the request was accepted.</summary>
    public DateTimeOffset ScheduledAtUtc { get; set; }
    /// <summary>Current durable lifecycle state.</summary>
    public ScheduleStatus Status { get; set; }
    /// <summary>Number of dispatch attempts.</summary>
    public int AttemptCount { get; set; }
    /// <summary>UTC instant when a retry becomes eligible.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    /// <summary>Current lease owner.</summary>
    public string? LeaseOwner { get; set; }
    /// <summary>UTC lease expiration.</summary>
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    /// <summary>Monotonic claim token used to reject stale owners.</summary>
    public long FencingToken { get; set; }
    /// <summary>Last dispatch failure without payload data.</summary>
    public string? LastError { get; set; }
    /// <summary>UTC completion instant.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
    /// <summary>Logical transport name used for audit data.</summary>
    public string Transport { get; set; } = string.Empty;
    /// <summary>Selected scheduling mechanism.</summary>
    public SchedulingStrategy Strategy { get; set; }
    /// <summary>Serialized scheduled message.</summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    /// <summary>Serializer and transport-independent metadata.</summary>
    public Dictionary<string, object?> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional serialized request required for targeted response dispatch.</summary>
    public byte[]? RequestPayload { get; set; }
    /// <summary>Assembly-qualified request type for targeted response dispatch.</summary>
    public string? RequestType { get; set; }
    /// <summary>Serializer metadata for <see cref="RequestPayload"/>.</summary>
    public Dictionary<string, object?>? RequestHeaders { get; set; }
    /// <summary>Broker resource created for this request, when applicable.</summary>
    public string? CreatedResourceId { get; set; }
    /// <summary>Whether the requested delay is a predefined canonical bucket.</summary>
    public bool IsPredefined { get; set; }
    /// <summary>Canonical bucket suffix, or a deterministic arbitrary-delay suffix.</summary>
    public string DelaySuffix { get; set; } = string.Empty;
    /// <summary>Stable delay or absolute-time intent used to validate ScheduleId retries.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>A schedule atomically claimed by a worker.</summary>
public sealed class ScheduleClaim
{
    /// <summary>Claimed durable record.</summary>
    public ScheduleRecord Record { get; set; } = new();
    /// <summary>Worker identifier that owns the lease.</summary>
    public string LeaseOwner { get; set; } = string.Empty;
    /// <summary>Fencing token that must accompany mutations.</summary>
    public long FencingToken { get; set; }
}

/// <summary>Outcome of an idempotent schedule creation attempt.</summary>
public sealed class ScheduleCreationResult
{
    /// <summary>Persisted scheduling identifier.</summary>
    public Guid ScheduleId { get; set; }
    /// <summary>True when this call created the record; false when the same request already existed.</summary>
    public bool Created { get; set; }
}

/// <summary>Transport-ready scheduling envelope used by optional native strategies.</summary>
public sealed class NativeScheduleEnvelope
{
    /// <summary>Durable scheduling record and serialized payload.</summary>
    public ScheduleRecord Record { get; set; } = new();
    /// <summary>Requested delay from the current clock instant.</summary>
    public TimeSpan Delay { get; set; }
}
