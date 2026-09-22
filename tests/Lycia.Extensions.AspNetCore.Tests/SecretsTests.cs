// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Extensions;
using Lycia.Persistence.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// Proves the strict secret-free contract from a real, fully wired application (AddLycia + a real
/// persistence provider configured with unmistakable secret sentinel values), not just by inspecting the
/// DTO's declared properties.
/// </summary>
public class SecretsTests
{
    private const string SecretPassword = "SUPER_SECRET_PASSWORD_123";
    private const string SecretHost = "redis-secret.example";
    private const string SecretToken = "broker-token-xyz";

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationId"] = "SecretsTestApp",
                // A value that would leak through if anything ever serialized raw configuration.
                ["Lycia:SomeUnrelatedSecret"] = SecretToken
            })
            .Build();

    [Fact]
    public async Task Response_Never_Contains_The_Configured_Connection_Strings_Credentials_Or_Addresses()
    {
        using var host = await TestHost.StartAsync(
            services =>
            {
                var builder = services.AddLycia(Configuration());
                builder.UsePersistence().WithRedisSagaStore(o =>
                    o.ConnectionString =
                        $"{SecretHost}:6379,password={SecretPassword},user=admin-{SecretToken}");
            },
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SecretPassword, body);
        Assert.DoesNotContain(SecretHost, body);
        Assert.DoesNotContain(SecretToken, body);
        Assert.DoesNotContain("password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin-", body, StringComparison.OrdinalIgnoreCase);
        // Only the provider *name*, never the connection string, may appear.
        Assert.Contains("\"provider\":\"Redis\"", body);
    }

    [Fact]
    public async Task Response_Never_Contains_A_Snapshot_Field_Carrying_Runtime_Or_Operational_Data()
    {
        using var host = await TestHost.StartAsync(
            services =>
            {
                var builder = services.AddLycia(Configuration());
                builder.UsePersistence().WithRedisSagaStore(o => o.ConnectionString = $"{SecretHost}:6379");
            },
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");
        var body = await response.Content.ReadAsStringAsync();

        // The response is a small, closed set of known-safe keys; nothing that could carry a payload,
        // SagaData, an Inbox/Outbox record, a journal entry, or a raw configuration section.
        foreach (var forbidden in new[]
                 {
                     "connectionString", "ConnectionString", "password", "Password", "sagaData", "SagaData",
                     "payload", "Payload", "messageId", "MessageId", "environment", "Environment",
                     "token", "Token"
                 })
        {
            Assert.DoesNotContain($"\"{forbidden}\"", body);
        }
    }
}
