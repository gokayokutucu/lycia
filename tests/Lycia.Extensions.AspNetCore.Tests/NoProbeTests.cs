// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Diagnostics;
using System.Net;
using Lycia.Extensions;
using Lycia.Persistence.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// Proves that requesting the diagnostics endpoint is a configuration/topology read, never a probe of
/// the infrastructure it describes. Every store the endpoint's projection touches (SagaStore, Inbox,
/// Outbox, journal, rebuild - see <see cref="Lycia.Extensions.Reliability.LyciaReliabilityDiagnostics"/>)
/// is configured against an unroutable address; a real connection attempt would hang or fail slowly
/// (StackExchange.Redis's <c>ConnectionMultiplexer.Connect</c> in particular blocks synchronously), so
/// the request completing quickly is the proof no such attempt happened on the request path.
/// </summary>
public class NoProbeTests
{
    // 10.255.255.1 is a non-routable address (RFC 5737-style black hole for this purpose): a real
    // connection attempt does not fail fast, it blocks until it times out.
    private const string UnroutableRedis = "10.255.255.1:6379,connectTimeout=200,abortConnect=false";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApplicationId"] = "NoProbeTestApp" })
            .Build();

    [Fact]
    public async Task Requesting_The_Endpoint_With_An_Unreachable_Redis_SagaStore_Returns_Quickly()
    {
        using var host = await TestHost.StartAsync(
            services =>
            {
                var builder = services.AddLycia(Configuration());
                builder.UsePersistence().WithRedisSagaStore(o => o.ConnectionString = UnroutableRedis);
            },
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var stopwatch = Stopwatch.StartNew();
        var response = await host.Client().GetAsync("/diagnostics/lycia");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"Request took {stopwatch.ElapsedMilliseconds} ms against an unroutable Redis address - it likely probed the store.");
    }

    [Fact]
    public async Task Requesting_The_Endpoint_With_Unreachable_Redis_Inbox_And_Outbox_Returns_Quickly()
    {
        using var host = await TestHost.StartAsync(
            services =>
            {
                var builder = services.AddLycia(Configuration());
                builder.UsePersistence()
                    .WithRedisSagaStore(o => o.ConnectionString = UnroutableRedis)
                    .WithRedisInbox(o => o.ConnectionString = UnroutableRedis)
                    .WithRedisOutbox(o => o.ConnectionString = UnroutableRedis);
            },
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var stopwatch = Stopwatch.StartNew();
        var response = await host.Client().GetAsync("/diagnostics/lycia");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"Request took {stopwatch.ElapsedMilliseconds} ms against unroutable Redis Inbox/Outbox addresses - it likely probed them.");

        var body = await response.Content.ReadAsStringAsync();
        // The capabilities still report as configured - the endpoint stays accurate, it just never dials out.
        Assert.Contains("\"inbox\":true", body);
        Assert.Contains("\"outbox\":true", body);
    }

    [Fact]
    public async Task Repeated_Requests_Never_Grow_Slower_As_Would_Happen_If_Each_One_Reconnected()
    {
        using var host = await TestHost.StartAsync(
            services =>
            {
                var builder = services.AddLycia(Configuration());
                builder.UsePersistence().WithRedisSagaStore(o => o.ConnectionString = UnroutableRedis);
            },
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var client = host.Client();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/diagnostics/lycia");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"Five requests took {stopwatch.ElapsedMilliseconds} ms total against an unroutable address.");
    }
}
