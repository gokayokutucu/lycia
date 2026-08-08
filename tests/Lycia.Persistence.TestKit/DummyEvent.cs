// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Utility;

namespace Lycia.Persistence.TestKit;

/// <summary>Minimal message type used by <see cref="SagaStoreConformanceTests"/>.</summary>
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

    public string Message { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public Guid ParentMessageId { get; set; }
    public Guid? CausationId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public Guid? SagaId { get; set; }
}

/// <summary>Minimal handler-identity marker used by <see cref="SagaStoreConformanceTests"/>.</summary>
public class DummySagaHandler;

/// <summary>Minimal <see cref="SagaData"/> used to exercise SaveSagaDataAsync/LoadSagaDataAsync round-trips.</summary>
public class DummySagaData : Lycia.Saga.Abstractions.Messaging.SagaData
{
    public string Payload { get; set; } = string.Empty;
    public int Counter { get; set; }
}
