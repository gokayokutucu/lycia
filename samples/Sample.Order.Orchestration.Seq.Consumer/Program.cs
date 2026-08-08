// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Extensions;
using Lycia.Extensions.RabbitMq;
using Lycia.Extensions.Logging;
using Lycia.Extensions.OpenTelemetry;
using Lycia.Extensions.Scheduling;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug() 
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: "order-orchestration-consumer",
            serviceVersion: "1.0.0"
        ))
    .AddLyciaTracing()
        .WithTracing(tp =>
    {
            tp.AddSource("Lycia");
            tp.AddAspNetCoreInstrumentation();
            tp.AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317"));
        });

builder.Services.AddLycia(builder.Configuration, lycia =>
{
    // Retry and event-bus outbox/retry-policy hooks (ConfigureRetry / ConfigureEventBus) remain available
    // as-is inside this callback; omitted here since this sample doesn't need them.
    lycia.ConfigureLogging(l =>
    {
        l.MinimumLevel = LogLevel.Debug;
        l.IncludeMessageHeaders = true;
        l.IncludeMessagePayload = true;
        l.PayloadMaxLength = 4096;
        l.RedactedHeaderKeys = ["Authorization", "X-Api-Key"];
        l.StartTemplate = "Handling {MessageType}";
        l.SuccessTemplate = "Handled {MessageType} successfully";
        l.ErrorTemplate = "Failed to handle {MessageType}";
    });

    lycia
        .AddSagas()
            .FromCurrentAssembly();

    lycia
        .UseTransport()
            .RabbitMq();

    lycia
        .UsePersistence()
            .WithRedisSagaStore();

    lycia
        .AddScheduling()
            .WithRedisStore()
            .WithPredefinedDelays()
            .WithVacuum(v => v.ApplicationTopology.Mode = Lycia.Scheduling.VacuumMode.ReportOnly);

    lycia
        .AddMiddleware()
            .WithLogging<SerilogLoggingMiddleware>();
});

var host = builder.Build();
await host.RunAsync();
