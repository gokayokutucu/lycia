// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Testcontainers.RabbitMq;

namespace Lycia.IntegrationTests;

/// <summary>
/// One real RabbitMQ broker shared by a test class. The image comes from
/// infrastructure-versions.json (or <c>LYCIA_TEST_RABBITMQ_IMAGE</c>), so the same tests run against the minimum or the current broker.
/// The AMQP port is bound to a fixed host port so a broker restart keeps the same address, which is what
/// lets automatic connection recovery be exercised.
/// </summary>
public sealed class RabbitMqBrokerFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;
    private int _hostPort;

    public string Image { get; } = Lycia.Tests.Infrastructure.InfrastructureVersions.Image("rabbitmq");

    public string Host => _container!.Hostname;
    public int Port => _hostPort;
    public string ConnectionString => $"amqp://guest:guest@{Host}:{Port}";

    public async Task InitializeAsync()
    {
        _hostPort = FreeTcpPort();
        _container = new RabbitMqBuilder()
            .WithImage(Image)
            .WithUsername("guest")
            .WithPassword("guest")
            .WithPortBinding(_hostPort, 5672)
            .WithCleanUp(true)
            .Build();
        await _container.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Restarts the broker application inside the running container: connections drop, the port stays.</summary>
    public async Task RestartBrokerAsync()
    {
        await ExecAsync("rabbitmqctl", "stop_app").ConfigureAwait(false);
        await Task.Delay(1000).ConfigureAwait(false);
        await ExecAsync("rabbitmqctl", "start_app").ConfigureAwait(false);
    }

    public Task StopBrokerAppAsync() => ExecAsync("rabbitmqctl", "stop_app");

    public Task StartBrokerAppAsync() => ExecAsync("rabbitmqctl", "start_app");

    private async Task ExecAsync(params string[] command)
    {
        var result = await _container!.ExecAsync(command).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{string.Join(' ', command)}' failed: {result.Stderr}");
    }

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try { return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
