// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Extensions.OpenTelemetry;
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

builder.Services.AddOpenTelemetry()
    .AddLyciaTracing()
    .WithTracing(tp =>
    {
        tp.AddAspNetCoreInstrumentation();
        tp.AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317"));
    });

builder.Services
    //.AddLycia(builder.Configuration)
    // .AddLycia(o=>
    // {
    //     o.ConfigureRetry(r =>
    //     {
    //         r.MaxRetryAttempts = 5;
    //         r.BackoffType      = DelayBackoffType.Exponential;
    //         r.Delay            = TimeSpan.FromMilliseconds(500);
    //         r.MaxDelay         = TimeSpan.FromSeconds(10);
    //         r.UseJitter        = true;
    //
    //         r.ShouldHandle = new PredicateBuilder()
    //             .Handle<TransientSagaException>()
    //             .Handle<TimeoutException>();
    //     });
    //     o.ConfigureEventBus(b =>
    //     {
    //         b.WithOutbox<EfCoreOutboxStore>()
    //             .WithRetryPolicy<PollyBasedRetry>();
    //     });
    // }, builder.Configuration)
    .AddLycia(o=>
    {
        o.ConfigureLogging(l =>
        {
            l.MinimumLevel = LogLevel.Debug;
            l.IncludeMessageHeaders = true;
            l.IncludeMessagePayload = true;
            l.PayloadMaxLength = 4096;
            l.RedactedHeaderKeys = ["Authorization", "X-Api-Key"];
            l.StartTemplate   = "Handling {MessageType}";
            l.SuccessTemplate = "Handled {MessageType} successfully";
            l.ErrorTemplate   = "Failed to handle {MessageType}";
        });
        o.UseLoggingMiddleware<SerilogLoggingMiddleware>();
    }, builder.Configuration)
    //.UseMessageSerializer<MyCustomSerializer>()
    // .UseSagaMiddleware(opt =>
    // {
    //      opt.AddMiddleware<SerilogLoggingMiddleware>();
    //     //opt.AddMiddleware<RetryMiddleware>();
    // })
    .AddSagasFromCurrentAssembly()
    .Build();

var host = builder.Build();
await host.RunAsync();