// Copyright 2025 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Observability;

using OpenTelemetry.Context.Propagation;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

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
                if (!carrier.TryGetValue(key, out var raw)) return [];

                // Live RabbitMQ delivery gives the byte[] written by Inject directly. A header that
                // passed through the durable Outbox envelope (JSON round-trip via Newtonsoft) loses
                // that runtime type: byte[] serializes as a Base64 JSON string, and generic object?
                // deserialization has no schema to recover it as byte[] again, so it comes back as a
                // plain string instead. Handle both so trace context survives Outbox-mediated hops too.
                return raw switch
                {
                    byte[] bytes => [Encoding.UTF8.GetString(bytes)],
                    string base64 when TryDecodeBase64Utf8(base64, out var decoded) => [decoded],
                    string plain => [plain],
                    _ => []
                };
            });

        return context.ActivityContext;
    }

    private static bool TryDecodeBase64Utf8(string base64, out string decoded)
    {
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }
}
