// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

namespace Lycia.Saga.Abstractions.Outbox;

/// <summary>
/// Versioned, transport-neutral representation of one outgoing operation. The serialized body and
/// headers are produced by the configured message serializer, so dispatch does not invent a second
/// payload format. Response envelopes also retain the original request required for targeted routing.
/// </summary>
public sealed class OutboxEnvelope
{
    /// <summary>Gets the envelope format version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Gets or sets the stable outbox identity. It is never regenerated during retry.</summary>
    public Guid OutboxId { get; set; }

    /// <summary>Gets or sets the stable message identity.</summary>
    public Guid MessageId { get; set; }

    /// <summary>Gets or sets the event-bus semantic to restore.</summary>
    public OutboxOperationKind Operation { get; set; }

    /// <summary>Gets or sets the assembly-qualified outgoing message type.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serializer-produced message body.</summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets serializer-produced message headers.</summary>
    public Dictionary<string, object?> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the optional handler type associated with the originating saga step.</summary>
    public string? HandlerType { get; set; }

    /// <summary>Gets or sets the canonical application identity captured at enqueue time.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Gets or sets the saga identity captured at enqueue time.</summary>
    public Guid? SagaId { get; set; }

    /// <summary>Gets or sets the request type required by a response operation.</summary>
    public string? RequestType { get; set; }

    /// <summary>Gets or sets the serializer-produced request body for a response operation.</summary>
    public byte[]? RequestBody { get; set; }

    /// <summary>Gets or sets serializer-produced request headers for a response operation.</summary>
    public Dictionary<string, object?>? RequestHeaders { get; set; }
}
