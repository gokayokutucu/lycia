using Lycia.Observability;
using OpenTelemetry;

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
    public static OpenTelemetryBuilder AddLyciaInstrumentation(
        this OpenTelemetryBuilder builder)
    {
        // Traces
        builder.WithTracing(tp => tp.AddSource(LyciaActivitySourceHolder.Name));

        // Metrics
        // builder.WithMetrics(mp => mp.AddMeter("Lycia.Saga"));

        return builder;
    }
}