// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions;
using Lycia.Extensions.Logging;
using Lycia.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddLycia(builder.Configuration)
    .UseSagaMiddleware(opt =>
    {
        opt.AddMiddleware<SerilogLoggingMiddleware>();
        //opt.AddMiddleware<RetryMiddleware>();
    })
    .AddSagasFromCurrentAssembly()
    .Build();

var host = builder.Build();
await host.RunAsync();