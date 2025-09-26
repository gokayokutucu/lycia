// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions;
using Lycia.Exceptions;
using Lycia.Extensions.Logging;
using Polly;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services
    .AddLycia(builder.Configuration)
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
    // }, builder.Configuration)
    // .AddLycia(o=>
    // {
    //     o.ConfigureEventBus(b =>
    //     {
    //         b.WithOutbox<EfCoreOutboxStore>()
    //             .WithRetryPolicy<PollyBasedRetry>();
    //     });
    // }, builder.Configuration)
    //.UseMessageSerializer<MyCustomSerializer>()
    .UseSagaMiddleware(opt =>
    {
         opt.AddMiddleware<SerilogLoggingMiddleware>();
        //opt.AddMiddleware<RetryMiddleware>();
    })
    .AddSagasFromCurrentAssembly()
    .Build();

var host = builder.Build();
await host.RunAsync();