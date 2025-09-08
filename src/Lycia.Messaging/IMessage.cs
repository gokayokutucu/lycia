// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Messaging;

public interface IMessage
{
    /// <summary>
    /// Unique ID for this message instance (deduplication, replay-safe).
    /// </summary>
    Guid MessageId { get; }
    
    /// <summary>
    /// Unique ID for this parent message instance (deduplication, replay-safe). Equivalents to CausationId. 
    /// </summary>
    Guid ParentMessageId { get; } // CausationId

    /// <summary>
    /// Correlates this message with a logical operation, transaction, or saga flow.
    /// All messages within the same workflow should have the same CorrelationId.
    /// </summary>
    Guid CorrelationId { get; set; }

    /// <summary>
    /// Creation or dispatch time (for ordering, debugging).
    /// </summary>
    DateTime Timestamp { get; }

    /// <summary>
    /// The application (or service) that published this message.
    /// </summary>
    string ApplicationId { get; }

    /// <summary>
    /// Optional saga instance identifier (if used in saga flows).
    /// </summary>
    Guid? SagaId { get; set; }
}

public interface ICommand : IMessage {}
public interface IEvent : IMessage {}