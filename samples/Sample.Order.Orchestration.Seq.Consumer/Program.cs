// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddLycia(builder.Configuration)
    // .AddLycia(o=>
    // {
    //     o.ConfigureEventBus(b =>
    //     {
    //         b.WithOutbox<EfCoreOutboxStore>()
    //             .WithRetryPolicy<PollyBasedRetry>();
    //     });
    // }, builder.Configuration)
    .AddSagasFromCurrentAssembly()
    .Build();

var host = builder.Build();
await host.RunAsync();