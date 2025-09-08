// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Messaging.Utility;

namespace Lycia.Tests.Messages;
public class DummyGrandparentEvent: DummyEvent{}
public class DummyParentEvent: DummyEvent{}
public class DummyChildEvent: DummyEvent{}
// Dummy types for test isolation
public class DummyEvent : IMessage
{
    public DummyEvent()
    {
    }

    public DummyEvent(Guid? parentMessageId = null, Guid? correlationId = null, string? applicationId = null)
    {
        MessageId = GuidV7.NewGuidV7();
        ParentMessageId = parentMessageId ?? Guid.Empty;
        CorrelationId = correlationId ?? MessageId;
        Timestamp = DateTime.UtcNow;
        ApplicationId = applicationId ?? EventMetadata.ApplicationId;
    }
    
    public bool IsCompensationFailed { get; set; } = false;
    public bool IsFailed { get; set; } = false;

    public string Message { get; set; }

    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; }
    public Guid? SagaId { get; set; }
}