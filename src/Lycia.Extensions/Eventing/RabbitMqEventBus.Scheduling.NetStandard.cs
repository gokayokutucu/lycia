// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

#if NETSTANDARD2_0
using Lycia.Extensions.Helpers;
using Lycia.Helpers;
using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Extensions.Eventing;

public sealed partial class RabbitMqEventBus
{
    /// <inheritdoc />
    public string TransportName => "rabbitmq";

    /// <inheritdoc />
    public Task<bool> CanScheduleAsync(NativeScheduleEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(envelope.Delay > TimeSpan.Zero &&
                               envelope.Delay <= RabbitMqSchedulingTopology.MaximumNativeDelay);
    }

    /// <inheritdoc />
    public async Task<string?> ScheduleNativeAsync(NativeScheduleEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        if (envelope.Delay <= TimeSpan.Zero || envelope.Delay > RabbitMqSchedulingTopology.MaximumNativeDelay)
            throw new NotSupportedException(
                $"RabbitMQ native delay must be positive and no greater than {RabbitMqSchedulingTopology.MaximumNativeDelay}.");
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        if (_channel == null) throw new InvalidOperationException("RabbitMQ channel is unavailable for scheduling.");
        var record = envelope.Record;
        var messageType = Type.GetType(record.MessageType, throwOnError: true)!;
        var finalExchange = MessagingNamingHelper.GetExchangeName(messageType);
        var finalExchangeType = RabbitMqTopology.GetExchangeType(messageType);
        var finalRoutingKey = record.MessageKind == ScheduledMessageKind.Event ? string.Empty : record.Destination;
        var queueName = RabbitMqSchedulingTopology.GetQueueName(record);
        var ttlMilliseconds = RabbitMqSchedulingTopology.GetTtlMilliseconds(envelope.Delay);
        var arguments = RabbitMqSchedulingTopology.CreateQueueArguments(
            ttlMilliseconds, finalExchange, finalRoutingKey, record.IsPredefined);

        await Task.Run(() => _channel.ExchangeDeclare(finalExchange, finalExchangeType, durable: true,
            autoDelete: false, arguments: null), cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => _channel.QueueDeclare(queueName, durable: true, exclusive: false,
                autoDelete: false, arguments: arguments), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"RabbitMQ scheduling queue '{queueName}' exists with incompatible TTL or dead-letter arguments.",
                exception);
        }

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = record.MessageId.ToString("D");
        properties.CorrelationId = record.ScheduleId.ToString("D");
        properties.Headers = RabbitMqSchedulingTopology.ToRabbitHeaders(record.Headers);
        await Task.Run(() => _channel.BasicPublish(string.Empty, queueName, mandatory: true, properties,
            record.Payload), cancellationToken).ConfigureAwait(false);
        record.Strategy = SchedulingStrategy.RabbitMqTtlDeadLetter;
        return queueName;
    }

    /// <inheritdoc />
    public async Task<SchedulingResourceState> InspectAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default)
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        if (_channel == null) throw new InvalidOperationException("RabbitMQ channel is unavailable for inspection.");
        try
        {
            var state = await Task.Run(() => _channel.QueueDeclarePassive(resource.CanonicalName), cancellationToken)
                .ConfigureAwait(false);
            return new SchedulingResourceState
            {
                Exists = true,
                MessageCount = state.MessageCount,
                ConsumerCount = state.ConsumerCount,
                OwnershipProven = resource.ManagementMode == SchedulingResourceManagementMode.DynamicScheduling,
                IsProtected = resource.IsPredefined || resource.ManagementMode == SchedulingResourceManagementMode.Protected
            };
        }
        catch
        {
            return new SchedulingResourceState { Exists = false, OwnershipProven = true };
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteConditionallyAsync(SchedulingResourceRecord resource,
        CancellationToken cancellationToken = default)
    {
        await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);
        if (_channel == null) return false;
        try
        {
            await Task.Run(() => _channel.QueueDelete(resource.CanonicalName, ifUnused: true, ifEmpty: true),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
#endif
