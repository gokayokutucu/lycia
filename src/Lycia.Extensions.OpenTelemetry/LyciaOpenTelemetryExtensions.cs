using Lycia.Observability;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Lycia.Extensions.OpenTelemetry;

/// <summary>
/// Provides extension methods for integrating Lycia-specific instrumentation
/// with the OpenTelemetry library.
/// </summary>
/// <remarks>
/// This class is designed to simplify the process of adding Lycia-specific tracing
/// to the OpenTelemetry pipeline. The extension methods within this class enable
/// streamlined configuration for monitoring and observability purposes.
/// </remarks>
public static class LyciaOpenTelemetryExtensions
{
    /// <summary>
    /// Adds Lycia-specific instrumentation for tracing and other observability features
    /// to the OpenTelemetry pipeline.
    /// </summary>
    /// <param name="builder">
    /// The <see cref="OpenTelemetryBuilder"/> instance used to configure the OpenTelemetry pipeline.
    /// </param>
    /// <returns>
    /// The modified <see cref="OpenTelemetryBuilder"/> instance with Lycia instrumentation configured.
    /// </returns>
    public static OpenTelemetryBuilder AddLyciaTracing(
        this OpenTelemetryBuilder builder)
    {
        // Lycia's cross-transport propagation helper (LyciaTracePropagation, used to carry W3C trace
        // context across RabbitMQ/NATS/Kafka message boundaries) reads and writes through
        // Propagators.DefaultTextMapPropagator. The OpenTelemetry SDK does not set this global default
        // on its own, so without this call every message hop silently starts a disconnected root trace
        // instead of continuing the caller's trace - Inject/Extract no-op against the SDK's default
        // no-op propagator.
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([
            new TraceContextPropagator(),
            new BaggagePropagator()
        ]));

        // Traces
        builder.WithTracing(tp => tp.AddSource(LyciaActivitySourceHolder.Name));

        // Metrics
        // builder.WithMetrics(mp => mp.AddMeter("Lycia.Saga"));

        return builder;
    }
}