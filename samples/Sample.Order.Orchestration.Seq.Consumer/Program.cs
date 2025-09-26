// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Extensions.Scheduling.Redis;
using Lycia.Saga.Exceptions;
using Polly;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services
    //.AddLycia(builder.Configuration)
    .AddLycia(o=>
    {
        o.ConfigureRetry(r =>
        {
            r.MaxRetryAttempts = 5;
            r.BackoffType      = DelayBackoffType.Exponential;
            r.Delay            = TimeSpan.FromMilliseconds(500);
            r.MaxDelay         = TimeSpan.FromSeconds(10);
            r.UseJitter        = true;
    
            r.ShouldHandle = new PredicateBuilder()
                .Handle<TransientSagaException>()
                .Handle<TimeoutException>();
        });
        o.ConfigureScheduling(s =>
        {
                s.UseRedis()
                .SetPollInterval(TimeSpan.FromSeconds(2))
                .SetBatchSize(200);
        });
    }, builder.Configuration)
    // .AddLycia(o=>
    // {
    //     o.ConfigureEventBus(b =>
    //     {
    //         b.WithOutbox<EfCoreOutboxStore>()
    //             .WithRetryPolicy<PollyBasedRetry>();
    //     });
    //     o.ConfigureScheduling(s =>
    //     {
    //         s.UseRedis(configSection: "Lycia:Scheduling:Redis"); // connection string, appId
    //         s.PollInterval(TimeSpan.FromSeconds(1));
    //         s.BatchSize(100);
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