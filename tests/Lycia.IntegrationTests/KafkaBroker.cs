// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Kafka;

namespace Lycia.IntegrationTests;

/// <summary>
/// A single-node, KRaft-mode Kafka broker for the integration tests. Confluent images are started through the
/// Testcontainers Kafka module; Apache Kafka images (<c>apache/kafka</c>) are started directly, because that
/// module launches Confluent's own entrypoint, which Apache's image does not contain. The image comes from
/// infrastructure-versions.json, so the same tests run against the minimum or the current broker.
/// </summary>
public sealed class KafkaBroker : IAsyncDisposable
{
    private readonly IContainer? _apache;
    private readonly KafkaContainer? _confluent;
    private readonly string _apacheBootstrap = string.Empty;

    public KafkaBroker(string image)
    {
        Image = image;
        if (image.StartsWith("apache/kafka", StringComparison.Ordinal))
        {
            var port = FreeTcpPort();
            _apacheBootstrap = $"localhost:{port}";
            // One listener serves clients and the broker itself: it listens on the same port it advertises, and that
            // port is published unchanged, so the advertised address is reachable from the host and the container.
            _apache = new ContainerBuilder()
                .WithImage(image)
                .WithPortBinding(port, port)
                .WithEnvironment("KAFKA_NODE_ID", "1")
                .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                .WithEnvironment("KAFKA_LISTENERS", $"PLAINTEXT://:{port},CONTROLLER://:9093")
                .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"PLAINTEXT://localhost:{port}")
                .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT")
                .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
                .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
                .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
                .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
                .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
                .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
                .WithCleanUp(true)
                .Build();
        }
        else
        {
            _confluent = new KafkaBuilder().WithImage(image).WithCleanUp(true).Build();
        }
    }

    public string Image { get; }

    public string BootstrapServers => _apache != null ? _apacheBootstrap : _confluent!.GetBootstrapAddress();

    public Task StartAsync() => _apache?.StartAsync() ?? _confluent!.StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_apache != null) await _apache.DisposeAsync().ConfigureAwait(false);
        if (_confluent != null) await _confluent.DisposeAsync().ConfigureAwait(false);
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
