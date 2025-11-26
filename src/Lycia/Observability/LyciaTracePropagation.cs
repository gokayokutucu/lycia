// Copyright 2025 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Observability;

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using OpenTelemetry.Context.Propagation;

/// <summary>
/// Helper for injecting and extracting W3C trace context to/from RabbitMQ headers.
/// </summary>
public static class LyciaTracePropagation
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    /// <summary>
    /// Injects the current Activity and Baggage into the given AMQP headers dictionary.
    /// Values are written as UTF8 byte[] to align with RabbitMQ header conventions.
    /// </summary>
    /// <param name="headers">Carrier headers dictionary.</param>
    public static void Inject(IDictionary<string, object?> headers)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        Propagator.Inject(
            new PropagationContext(activity.Context, default),
            headers,
            static (carrier, key, value) =>
            {
                carrier[key] = Encoding.UTF8.GetBytes(value);
            });
    }

    /// <summary>
    /// Extracts an ActivityContext from the given AMQP headers dictionary.
    /// Returns default(ActivityContext) if not present.
    /// </summary>
    public static ActivityContext Extract(IDictionary<string, object?> headers)
    {
        var context = Propagator.Extract(
            default(PropagationContext),
            headers,
            static (carrier, key) =>
            {
                if (!carrier.TryGetValue(key, out var raw) || raw is not byte[] bytes)
                    return [];

                return [Encoding.UTF8.GetString(bytes)];
            });

        return context.ActivityContext;
    }
}
