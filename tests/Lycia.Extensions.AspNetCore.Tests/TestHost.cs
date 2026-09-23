// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// Builds a minimal in-memory ASP.NET Core host (<see cref="TestServer"/>) for endpoint tests, without a
/// real network listener. The caller fully controls DI registration and the middleware pipeline, so each
/// test can compose exactly the routing/authentication/authorization pipeline it needs to exercise.
/// </summary>
internal static class TestHost
{
    public static async Task<IHost> StartAsync(
        Action<IServiceCollection> configureServices,
        Action<IApplicationBuilder> configureApp)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    configureServices(services);
                });
                webBuilder.Configure(configureApp);
            })
            .StartAsync();
        return host;
    }

    public static HttpClient Client(this IHost host) => host.GetTestClient();
}
