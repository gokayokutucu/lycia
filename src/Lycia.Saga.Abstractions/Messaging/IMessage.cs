// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Messaging;

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

/// <summary>
/// Identifies a transport-independent logical owner for a command contract.
/// Application command endpoint interfaces inherit this marker and <see cref="ICommand"/>.
/// </summary>
/// <example>
/// <code>
/// public interface IStockServiceCommand : ICommand, ICommandEndpoint { }
/// </code>
/// </example>
public interface ICommandEndpoint { }

/// <summary>
/// Resolves the single logical command owner declared by a command contract.
/// </summary>
public interface ICommandEndpointResolver
{
    /// <summary>Returns the stable logical endpoint name for <paramref name="commandType"/>.</summary>
    string Resolve(Type commandType);
}

/// <summary>
/// Request metadata used by transports to target a response without creating a destination per saga instance.
/// </summary>
public interface IRequestRoutingMetadata
{
    /// <summary>Identifies the original request across its response flow.</summary>
    Guid RequestId { get; set; }

    /// <summary>Gets or sets the logical application to which a response must return.</summary>
    string? ReplyTo { get; set; }
}
