// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Logging;
using Lycia.Extensions;
using Lycia.Extensions.RabbitMq;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLycia(builder.Configuration, lycia =>
{
    lycia
        .AddSagas()
            .FromCurrentAssembly();

    lycia
        .UseTransport()
            .RabbitMq();

    lycia
        .AddMiddleware()
            .WithLogging<SerilogLoggingMiddleware>();
});

var host = builder.Build();
await host.RunAsync();
