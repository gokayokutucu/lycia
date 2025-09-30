namespace Lycia.Saga.Abstractions;

/// <summary>
/// Health check contract for saga store providers (e.g., Redis/DB). Implement a lightweight round-trip.
/// </summary>
public interface ISagaStoreHealthCheck : ISagaHealthCheck;

/// <summary>
/// Health check contract for serializers / schema registry integration.
/// </summary>
public interface ISerializerHealthCheck : ISagaHealthCheck;

/// <summary>
/// Health check contract for event bus providers (e.g., RabbitMQ, Kafka).
/// </summary>
public interface IEventBusHealthCheck : ISagaHealthCheck;

/// <summary>
/// Health check contract for outbox persistence and publisher.
/// </summary>
public interface IOutboxHealthCheck : ISagaHealthCheck;

/// <summary>
/// Base health check contract used by Lycia aggregators.
/// </summary>
public interface ISagaHealthCheck
{
    Task<bool> PingAsync(CancellationToken cancellationToken);
}
