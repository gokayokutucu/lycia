using Lycia.Extensions;
using Lycia.Saga.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddLycia(builder.Configuration)
    .AddSagasFromCurrentAssembly()
    .Build();

var host = builder.Build();
await host.RunAsync();