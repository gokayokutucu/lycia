// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using System.Text;
using Lycia.Messaging;
using Lycia.Saga.Abstractions.Scheduling;

namespace Lycia.Extensions.Helpers;

/// <summary>Canonical RabbitMQ TTL and dead-letter scheduling topology.</summary>
public static class RabbitMqSchedulingTopology
{
    /// <summary>Largest TTL representable by RabbitMQ's unsigned 32-bit millisecond field.</summary>
    public static readonly TimeSpan MaximumNativeDelay = TimeSpan.FromMilliseconds(uint.MaxValue);

    /// <summary>Builds a deterministic Lycia-owned delay queue name.</summary>
    public static string GetQueueName(ScheduleRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        var type = Type.GetType(record.MessageType, throwOnError: true)!;
        var scope = record.IsPredefined ? "predefined" : "dynamic";
        var kind = record.MessageKind.ToString().ToLowerInvariant();
        var destination = EndpointIdentityNormalizer.Default.Normalize(record.Destination);
        return $"lycia.schedule.{scope}.{kind}.{type.Name.ToLowerInvariant()}.{destination}.{record.DelaySuffix}";
    }

    /// <summary>Returns a positive whole-millisecond TTL supported by RabbitMQ.</summary>
    public static long GetTtlMilliseconds(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        return checked((long)Math.Ceiling(delay.TotalMilliseconds));
    }

    /// <summary>Creates fixed TTL, dead-letter routing, and optional dynamic expiry arguments.</summary>
    public static IDictionary<string, object?> CreateQueueArguments(long ttlMilliseconds, string finalExchange,
        string finalRoutingKey, bool predefined)
    {
        if (ttlMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(ttlMilliseconds));
        var arguments = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = ttlMilliseconds,
            ["x-dead-letter-exchange"] = finalExchange,
            ["x-dead-letter-routing-key"] = finalRoutingKey
        };
        var expires = checked(ttlMilliseconds + (long)TimeSpan.FromDays(7).TotalMilliseconds);
        if (!predefined && expires <= uint.MaxValue)
            arguments["x-expires"] = expires;
        return arguments;
    }

    /// <summary>Converts serializer headers to RabbitMQ-compatible scalar or byte-array values.</summary>
    public static IDictionary<string, object?> ToRabbitHeaders(IReadOnlyDictionary<string, object?> headers)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            if (pair.Value == null) continue;
            result[pair.Key] = pair.Value is string text ? Encoding.UTF8.GetBytes(text) : pair.Value;
        }
        return result;
    }
}
