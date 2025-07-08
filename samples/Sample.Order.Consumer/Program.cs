using Lycia.Extensions;
using Lycia.Saga.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddLycia(builder.Configuration)
    .AddSagasFromCurrentAssembly();

var host = builder.Build();
host.Run();