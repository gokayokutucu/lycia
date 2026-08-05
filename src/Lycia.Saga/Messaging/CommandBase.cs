// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Utility;

namespace Lycia.Saga.Messaging;

public abstract class CommandBase: ICommand, IRequestRoutingMetadata
{
    protected CommandBase()
    {
        SagaId = Guid.Empty;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty;
        CausationId = null;
        CorrelationId = MessageId;
        RequestId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }
    
    protected CommandBase(Guid? sagaId = null)
    {
        SagaId = sagaId;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = Guid.Empty;
        CausationId = null;
        CorrelationId = MessageId;
        RequestId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    
    protected CommandBase(Guid? sagaId = null, Guid? parentMessageId = null, Guid? correlationId = null)
    {
        SagaId = sagaId;
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CausationId = null;
        CorrelationId = correlationId ?? MessageId;
        RequestId = MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId  = EventMetadata.ApplicationId;
    }

    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
    public Guid? SagaId { get; set; }
    /// <inheritdoc />
    public Guid RequestId { get; set; }
    /// <inheritdoc />
    public string? ResponseEndpoint { get; set; }
    /// <inheritdoc />
    [Obsolete("Use ResponseEndpoint.")]
    public string? ReplyTo
    {
        get => ResponseEndpoint;
        set => ResponseEndpoint = value;
    }
}
