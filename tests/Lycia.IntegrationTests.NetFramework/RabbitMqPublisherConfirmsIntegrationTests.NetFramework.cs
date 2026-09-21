// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Net;
using System.Net.Sockets;
using System.Text;
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Serialization;
using Lycia.Helpers;
using Lycia.Messaging;
using Lycia.Observability;
using Lycia.Outbox;
using Lycia.Persistence.InMemory;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Lycia.IntegrationTests;

/// <summary>Raw RabbitMQ 6.x access for arranging broker state and reading back what actually arrived.</summary>
public sealed class BrokerProbeNetFramework : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public BrokerProbeNetFramework(string uri)
    {
        _connection = new ConnectionFactory { Uri = new Uri(uri) }.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public void BindQueue(string queue, string exchange, string exchangeType, string routingKey,
        IDictionary<string, object>? queueArguments = null)
    {
        _channel.ExchangeDeclare(exchange, exchangeType, durable: true, autoDelete: false);
        _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: queueArguments);
        _channel.QueueBind(queue, exchange, routingKey);
    }

    public uint Count(string queue) => _channel.QueueDeclarePassive(queue).MessageCount;

    public List<Guid> DrainMessageIds(string queue)
    {
        var ids = new List<Guid>();
        BasicGetResult? message;
        while ((message = _channel.BasicGet(queue, autoAck: true)) != null)
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(message.Body.ToArray()));
            ids.Add(Guid.Parse((string)json.GetValue("MessageId", StringComparison.OrdinalIgnoreCase)!));
        }

        return ids;
    }

    public void DeleteQueue(string queue)
    {
        try { _channel.QueueDelete(queue); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// Publisher confirms through the netstandard2.0 transport, which is built on RabbitMQ.Client 6.8.1 (ConfirmSelect,
/// WaitForConfirms, BasicReturn) rather than the 7.x tracking API. The same guarantees are checked against a real
/// broker: confirmed publishes become Published, and failures and unknown outcomes never do.
/// </summary>
public class RabbitMqPublisherConfirmsNetFrameworkTests : IAsyncLifetime
{
    private static readonly NewtonsoftJsonMessageSerializer Serializer = new();

    private readonly bool _isCI = Environment.GetEnvironmentVariable("CI") == "true";
    private RabbitMqContainer? _container;

    private string ConnectionString { get; set; } = null!;

    public async Task InitializeAsync()
    {
        if (_isCI)
        {
            // CI runs against the broker the workflow installed; it cannot be restarted from a test.
            ConnectionString = Environment.GetEnvironmentVariable("LYCIA__EVENTBUS__CONNECTIONSTRING")
                                ?? "amqp://guest:guest@localhost:5672/";
            return;
        }

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        _container = new RabbitMqBuilder()
            .WithImage(Lycia.Tests.Infrastructure.InfrastructureVersions.Image("rabbitmq"))
            .WithUsername("guest")
            .WithPassword("guest")
            .WithPortBinding(port, 5672)
            .WithCleanUp(true)
            .Build();
        await _container.StartAsync();
        ConnectionString = $"amqp://guest:guest@{_container.Hostname}:{port}";
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    private Uri BrokerUri => new(ConnectionString);

    private async Task<RabbitMqEventBus> CreateBusAsync(string? connectionString = null, Action<EventBusOptions>? configure = null)
    {
        var options = new EventBusOptions { ApplicationId = "ConfirmsTestNet48", ConnectionString = connectionString ?? ConnectionString };
        configure?.Invoke(options);
        return await RabbitMqEventBus.CreateAsync(NullLogger<RabbitMqEventBus>.Instance,
            new Dictionary<string, (Type, Type)>(), options, Serializer);
    }

    private static OutboxDispatcher Dispatcher(IOutboxStore store, IEventBus bus) =>
        new(store, bus, Serializer, new LyciaActivitySourceHolder(), NullLogger<OutboxDispatcher>.Instance);

    private static OutboxOutgoingMessagePipeline Pipeline(IOutboxStore store) => new(store, Serializer);

    private static string CommandKey<T>() => MessagingNamingHelper.GetCommandRoutingKey(typeof(T));

    private static string Exchange<T>() => MessagingNamingHelper.GetExchangeName(typeof(T));

    private static string NewQueue() => "confirms48-" + Guid.NewGuid().ToString("N");

    private static Task<OutboxDispatchResult> Pass(OutboxDispatcher dispatcher) =>
        dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.Zero);

    public enum Operation
    {
        Send,
        Publish,
        Respond
    }

    [Theory]
    [InlineData(Operation.Send)]
    [InlineData(Operation.Publish)]
    [InlineData(Operation.Respond)]
    public async Task A_confirmed_publish_becomes_Published(Operation operation)
    {
        using var probe = new BrokerProbeNetFramework(ConnectionString);
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        var store = new InMemoryOutboxStore();
        Guid messageId;
        switch (operation)
        {
            case Operation.Send:
            {
                probe.BindQueue(queue, Exchange<ProbeCommand>(), ExchangeType.Direct, CommandKey<ProbeCommand>());
                var command = new ProbeCommand { Payload = "send" };
                await Pipeline(store).Send(command, null, null);
                messageId = command.MessageId;
                break;
            }
            case Operation.Publish:
            {
                probe.BindQueue(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
                var evt = new ProbeEvent { Payload = "publish" };
                await Pipeline(store).Publish(evt, null, null);
                messageId = evt.MessageId;
                break;
            }
            default:
            {
                var request = new ProbeCommand { Payload = "request" };
                RequestRouting.Prepare(request);
                var response = new ProbeResponse();
                probe.BindQueue(queue, Exchange<ProbeResponse>(), ExchangeType.Direct, request.ResponseEndpoint!);
                await Pipeline(store).Respond(request, response, null, null);
                messageId = response.MessageId;
                break;
            }
        }

        try
        {
            var result = await Pass(Dispatcher(store, bus));

            Assert.Equal(1, result.Published);
            Assert.Equal(0, result.ConfirmationUnknown);
            Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(messageId))!.Status);
            Assert.Equal(new List<Guid> { messageId }, probe.DrainMessageIds(queue));
        }
        finally
        {
            probe.DeleteQueue(queue);
        }
    }

    [Fact]
    public async Task A_nacked_publish_is_reported_and_is_not_Published()
    {
        using var probe = new BrokerProbeNetFramework(ConnectionString);
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        probe.BindQueue(queue, Exchange<RejectingCommand>(), ExchangeType.Direct, CommandKey<RejectingCommand>(),
            new Dictionary<string, object> { ["x-max-length"] = 1, ["x-overflow"] = "reject-publish" });
        try
        {
            await bus.Send(new RejectingCommand());

            await Assert.ThrowsAsync<RabbitMqPublishNackedException>(() => bus.Send(new RejectingCommand()));

            var store = new InMemoryOutboxStore();
            var captured = new RejectingCommand();
            await Pipeline(store).Send(captured, null, null);
            var result = await Pass(Dispatcher(store, bus));
            Assert.Equal(0, result.Published);
            Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(captured.MessageId))!.Status);
            Assert.Equal(1u, probe.Count(queue));
        }
        finally
        {
            probe.DeleteQueue(queue);
        }
    }

    /// <summary>
    /// A rejected publish must never be reported as confirmed, however fast publishes follow each other. The 6.x
    /// client's own WaitForConfirms can misreport a nack as an ack in exactly this situation, which is why the
    /// transport resolves confirmations from the ack and nack frames itself. Five confirmations, thirty-five
    /// nacks and five messages on the broker is the only correct outcome.
    /// </summary>
    [Fact]
    public async Task Every_rejected_publish_is_reported_as_rejected_under_rapid_publishing()
    {
        using var probe = new BrokerProbeNetFramework(ConnectionString);
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        probe.BindQueue(queue, Exchange<RejectingCommand>(), ExchangeType.Direct, CommandKey<RejectingCommand>(),
            new Dictionary<string, object> { ["x-max-length"] = 5, ["x-overflow"] = "reject-publish" });
        try
        {
            var confirmed = 0;
            var nacked = 0;
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    await bus.Send(new RejectingCommand());
                    confirmed++;
                }
                catch (RabbitMqPublishNackedException)
                {
                    nacked++;
                }
            }

            Assert.Equal(5, confirmed);
            Assert.Equal(35, nacked);
            Assert.Equal(5u, probe.Count(queue));
        }
        finally
        {
            probe.DeleteQueue(queue);
        }
    }

    [Fact]
    public async Task An_unroutable_command_is_returned_not_confirmed()
    {
        await using var bus = await CreateBusAsync();

        var exception = await Assert.ThrowsAsync<RabbitMqUnroutableMessageException>(() => bus.Send(new OrphanCommand()));

        Assert.Equal(Exchange<OrphanCommand>(), exception.Exchange);
    }

    [Fact]
    public async Task A_message_the_broker_accepted_but_whose_confirm_was_lost_is_unknown_and_republished_with_the_same_id()
    {
        using var probe = new BrokerProbeNetFramework(ConnectionString);
        using var proxy = new TcpFaultProxy(BrokerUri.Host, BrokerUri.Port).Start();
        await using var lossyBus = await CreateBusAsync(proxy.ConnectionString, o => o.PublisherConfirmTimeout = TimeSpan.FromSeconds(20));
        await using var healthyBus = await CreateBusAsync();
        var queue = NewQueue();
        probe.BindQueue(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "confirm lost" };
        await Pipeline(store).Publish(evt, null, null);
        try
        {
            await lossyBus.Publish(new ProbeEvent { Payload = "warm-up" });   // declares the exchange while replies flow
            probe.DrainMessageIds(queue);

            proxy.DropServerToClient(true);
            var attempt = Pass(Dispatcher(store, lossyBus));
            await WaitUntilAsync(() => probe.Count(queue) == 1u);   // the broker has it; the client is still waiting
            proxy.SeverConnections();
            var result = await attempt;

            Assert.Equal(0, result.Published);
            Assert.Equal(1, result.ConfirmationUnknown);

            var recovered = await Pass(Dispatcher(store, healthyBus));
            Assert.Equal(1, recovered.Published);
            Assert.Equal(new List<Guid> { evt.MessageId, evt.MessageId }, probe.DrainMessageIds(queue));
        }
        finally
        {
            probe.DeleteQueue(queue);
        }
    }

    [Fact]
    public async Task A_confirm_that_never_arrives_times_out_as_unknown()
    {
        using var proxy = new TcpFaultProxy(BrokerUri.Host, BrokerUri.Port).Start();
        await using var bus = await CreateBusAsync(proxy.ConnectionString, o => o.PublisherConfirmTimeout = TimeSpan.FromSeconds(2));
        await bus.Publish(new ProbeEvent { Payload = "warm-up" });

        proxy.DropServerToClient(true);
        var exception = await Assert.ThrowsAsync<RabbitMqPublishOutcomeUnknownException>(() => bus.Publish(new ProbeEvent()));

        Assert.Contains("no confirmation arrived", exception.Message);
    }

    [Fact]
    public async Task With_publisher_confirms_disabled_the_transport_keeps_the_legacy_unconfirmed_behavior()
    {
        await using var bus = await CreateBusAsync(configure: o => o.PublisherConfirms = false);
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "legacy" };
        await Pipeline(store).Publish(evt, null, null);

        Assert.False(bus.ConfirmationsAvailable);
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.ConfirmationUnknown);
        await bus.Send(new OrphanCommand());   // legacy: no mandatory flag, dropped without an error
    }

    /// <summary>
    /// After the broker restarts, the 6.x client's automatic recovery must bring the publish channel back with
    /// confirms selected: an unroutable command is still returned, which the client only reports while confirms
    /// are on. Needs container control, so it runs only where the test owns the broker.
    /// </summary>
    [Fact]
    public async Task Publisher_confirms_are_still_enforced_after_the_broker_restarts()
    {
        if (_container == null) return;   // CI installs its own broker and cannot restart it from a test

        await using var bus = await CreateBusAsync();
        await Assert.ThrowsAsync<RabbitMqUnroutableMessageException>(() => bus.Send(new OrphanCommand()));

        Assert.Equal(0, (await _container.ExecAsync(new[] { "rabbitmqctl", "stop_app" })).ExitCode);
        await Task.Delay(1000);
        Assert.Equal(0, (await _container.ExecAsync(new[] { "rabbitmqctl", "start_app" })).ExitCode);

        var deadline = DateTime.UtcNow.AddSeconds(90);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await bus.Send(new OrphanCommand());
                last = new InvalidOperationException("an unroutable command was accepted: confirms were lost after recovery");
            }
            catch (RabbitMqUnroutableMessageException)
            {
                return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(500);
        }

        throw new Xunit.Sdk.XunitException($"confirm behavior was not restored after the restart: {last}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException("condition was not met in time");
    }
}
