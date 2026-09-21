// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
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
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;

namespace Lycia.IntegrationTests;

/// <summary>Raw RabbitMQ access for arranging broker state and reading back what actually arrived.</summary>
public sealed class BrokerProbe : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private BrokerProbe(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<BrokerProbe> ConnectAsync(string uri)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(uri) }.CreateConnectionAsync();
        return new BrokerProbe(connection, await connection.CreateChannelAsync());
    }

    public async Task BindQueueAsync(string queue, string exchange, string exchangeType, string routingKey,
        IDictionary<string, object?>? queueArguments = null)
    {
        await _channel.ExchangeDeclareAsync(exchange, exchangeType, durable: true, autoDelete: false);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: queueArguments);
        await _channel.QueueBindAsync(queue, exchange, routingKey);
    }

    public async Task<uint> CountAsync(string queue) => (await _channel.QueueDeclarePassiveAsync(queue)).MessageCount;

    public async Task<List<Guid>> DrainMessageIdsAsync(string queue)
    {
        var ids = new List<Guid>();
        while (await _channel.BasicGetAsync(queue, autoAck: true) is { } message)
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(message.Body.ToArray()));
            ids.Add(Guid.Parse((string)json.GetValue("MessageId", StringComparison.OrdinalIgnoreCase)!));
        }

        return ids;
    }

    public async Task<BasicGetResult?> PeekAsync(string queue) => await _channel.BasicGetAsync(queue, autoAck: false);

    public async Task DeleteQueueAsync(string queue)
    {
        try { await _channel.QueueDeleteAsync(queue); } catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// The Outbox against a real RabbitMQ broker with publisher confirms: what a healthy publish settles as,
/// and how each outcome the transport can or cannot establish is reported.
/// </summary>
public class RabbitMqPublisherConfirmsIntegrationTests(RabbitMqBrokerFixture broker) : IClassFixture<RabbitMqBrokerFixture>
{
    private static readonly NewtonsoftJsonMessageSerializer Serializer = new();

    private string ConnectionString => broker.ConnectionString;

    private async Task<RabbitMqEventBus> CreateBusAsync(string? connectionString = null, Action<EventBusOptions>? configure = null)
    {
        var options = new EventBusOptions
        {
            ApplicationId = "ConfirmsTest",
            ConnectionString = connectionString ?? ConnectionString
        };
        configure?.Invoke(options);
        return await RabbitMqEventBus.CreateAsync(NullLogger<RabbitMqEventBus>.Instance,
            new Dictionary<string, (Type, Type)>(), options, Serializer);
    }

    private static OutboxDispatcher Dispatcher(IOutboxStore store, IEventBus bus) =>
        new(store, bus, Serializer, new LyciaActivitySourceHolder(), NullLogger<OutboxDispatcher>.Instance);

    private static OutboxOutgoingMessagePipeline Pipeline(IOutboxStore store) => new(store, Serializer);

    private static string CommandKey<T>() => MessagingNamingHelper.GetCommandRoutingKey(typeof(T));

    private static string Exchange<T>() => MessagingNamingHelper.GetExchangeName(typeof(T));

    private static string NewQueue() => "confirms-" + Guid.NewGuid().ToString("N");

    private async Task<BrokerProbe> ProbeAsync() => await BrokerProbe.ConnectAsync(ConnectionString);

    private static Task<OutboxDispatchResult> Pass(OutboxDispatcher dispatcher) =>
        dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.Zero);

    // --- A healthy publish is confirmed ---------------------------------------------------------------

    public enum Operation
    {
        Send,
        Publish,
        Respond
    }

    /// <summary>
    /// The behavior that used to be ConfirmationUnknown: a healthy RabbitMQ publish, through each of Send,
    /// Publish and Respond, is positively confirmed and the Outbox records it as Published. The message really
    /// is on the broker, once, with its own MessageId.
    /// </summary>
    [Theory]
    [InlineData(Operation.Send)]
    [InlineData(Operation.Publish)]
    [InlineData(Operation.Respond)]
    public async Task A_confirmed_publish_becomes_Published(Operation operation)
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        var store = new InMemoryOutboxStore();
        Guid messageId;

        switch (operation)
        {
            case Operation.Send:
            {
                await probe.BindQueueAsync(queue, Exchange<ProbeCommand>(), ExchangeType.Direct, CommandKey<ProbeCommand>());
                var command = new ProbeCommand { Payload = "send" };
                await Pipeline(store).Send(command, null, null);
                messageId = command.MessageId;
                break;
            }
            case Operation.Publish:
            {
                await probe.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
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
                await probe.BindQueueAsync(queue, Exchange<ProbeResponse>(), ExchangeType.Direct, request.ResponseEndpoint!);
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
            Assert.Equal([messageId], await probe.DrainMessageIdsAsync(queue));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    [Fact]
    public async Task The_bus_reports_confirmations_available_and_persists_the_message()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        try
        {
            Assert.True(bus.ConfirmationsAvailable);

            await bus.Publish(new ProbeEvent { Payload = "durable" });

            // A confirm is not delivery-mode by itself: the message must also be persistent for the broker to
            // write it to disk before confirming, and the queue durable for it to survive a restart.
            var message = await probe.PeekAsync(queue);
            Assert.NotNull(message);
            Assert.Equal(DeliveryModes.Persistent, message!.BasicProperties.DeliveryMode);
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    [Fact]
    public async Task Concurrent_publishes_are_all_confirmed_and_arrive_exactly_once()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        try
        {
            var events = Enumerable.Range(0, 40).Select(i => new ProbeEvent { Payload = i.ToString() }).ToList();

            await Task.WhenAll(events.Select(e => Task.Run(() => bus.Publish(e))));

            var arrived = await probe.DrainMessageIdsAsync(queue);
            Assert.Equal(events.Select(e => e.MessageId).OrderBy(x => x), arrived.OrderBy(x => x));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    // --- Known failures are never reported as confirmed -----------------------------------------------

    /// <summary>
    /// A real broker nack. RabbitMQ nacks a publish when the queue process cannot take it; a queue at its
    /// length limit with the reject-publish overflow behavior is the documented, deterministic way to get one.
    /// </summary>
    [Fact]
    public async Task A_nacked_publish_is_reported_and_is_not_Published()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<RejectingCommand>(), ExchangeType.Direct, CommandKey<RejectingCommand>(),
            new Dictionary<string, object?> { ["x-max-length"] = 1, ["x-overflow"] = "reject-publish" });
        try
        {
            await bus.Send(new RejectingCommand());           // fills the queue

            var rejected = new RejectingCommand();
            await Assert.ThrowsAsync<RabbitMqPublishNackedException>(() => bus.Send(rejected));

            // Through the Outbox: the nack is a failed attempt, never Published.
            var store = new InMemoryOutboxStore();
            var captured = new RejectingCommand();
            await Pipeline(store).Send(captured, null, null);
            var result = await Pass(Dispatcher(store, bus));
            Assert.Equal(0, result.Published);
            Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(captured.MessageId))!.Status);
            Assert.Equal(1u, await probe.CountAsync(queue));  // only the first message was ever accepted
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    /// <summary>
    /// A rejected publish must never be reported as confirmed, however fast publishes follow each other. A queue
    /// that holds exactly five messages and rejects the rest gives an exact expectation: five confirmations,
    /// thirty-five nacks, and five messages on the broker. A nack misreported as an ack shows up as a mismatch.
    /// </summary>
    [Fact]
    public async Task Every_rejected_publish_is_reported_as_rejected_under_rapid_publishing()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<RejectingCommand>(), ExchangeType.Direct, CommandKey<RejectingCommand>(),
            new Dictionary<string, object?> { ["x-max-length"] = 5, ["x-overflow"] = "reject-publish" });
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
            Assert.Equal(5u, await probe.CountAsync(queue));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    [Fact]
    public async Task An_unroutable_command_is_returned_not_confirmed()
    {
        await using var bus = await CreateBusAsync();

        var exception = await Assert.ThrowsAsync<RabbitMqUnroutableMessageException>(() => bus.Send(new OrphanCommand()));

        Assert.Equal(Exchange<OrphanCommand>(), exception.Exchange);
        Assert.Equal(CommandKey<OrphanCommand>(), exception.RoutingKey);
    }

    /// <summary>
    /// An unroutable Outbox message is never Published. Because the failure is retried, it is delivered once its
    /// owner's queue exists — a startup-order race that used to lose the command silently.
    /// </summary>
    [Fact]
    public async Task An_unroutable_outbox_message_is_retried_and_published_once_a_route_exists()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var store = new InMemoryOutboxStore();
        var command = new ProbeCommand { Payload = "late owner" };
        await Pipeline(store).Send(command, null, null);
        var dispatcher = Dispatcher(store, bus);
        var queue = NewQueue();

        var first = await Pass(dispatcher);
        Assert.Equal(0, first.Published);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(command.MessageId))!.Status);

        // The owner's queue appears; the next attempt goes through and is confirmed.
        await probe.BindQueueAsync(queue, Exchange<ProbeCommand>(), ExchangeType.Direct, CommandKey<ProbeCommand>());
        try
        {
            var second = await Pass(dispatcher);

            Assert.Equal(1, second.Published);
            Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(command.MessageId))!.Status);
            Assert.Equal([command.MessageId], await probe.DrainMessageIdsAsync(queue));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    [Fact]
    public async Task An_event_with_no_subscribers_is_confirmed_by_default()
    {
        // Events may legitimately have no subscriber, so by default RabbitMQ's confirm is enough. This is what a
        // confirm means: the broker took responsibility per its routing, not that any consumer received it.
        await using var bus = await CreateBusAsync();
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "nobody listens" };
        await Pipeline(store).Publish(evt, null, null);

        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(1, result.Published);
    }

    [Fact]
    public async Task An_event_with_no_subscribers_is_returned_when_routable_events_are_required()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync(configure: o => o.RequireRoutableEvents = true);
        var queue = NewQueue();
        // Make sure no queue is bound to the fanout exchange for this check.
        await using (var setup = await ProbeAsync()) await setup.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        await probe.DeleteQueueAsync(queue);

        await Assert.ThrowsAsync<RabbitMqUnroutableMessageException>(() => bus.Publish(new ProbeEvent()));
    }

    // --- Unavailable broker ---------------------------------------------------------------------------

    [Fact]
    public async Task A_broker_that_cannot_be_reached_fails_before_anything_is_published()
    {
        var store = new InMemoryOutboxStore();
        await using var proxy = new TcpFaultProxy(broker.Host, broker.Port).Start();
        await using var bus = await CreateBusAsync(proxy.ConnectionString);
        var evt = new ProbeEvent { Payload = "no broker" };
        await Pipeline(store).Publish(evt, null, null);

        // The broker disappears: nothing accepts connections and existing ones are cut.
        await proxy.DisposeAsync();

        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(0, result.Published);
        var row = (await store.GetByMessageIdAsync(evt.MessageId))!;
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, row.Status);
        Assert.Equal(1, row.RetryCount);
    }

    // --- The outcome cannot be established ------------------------------------------------------------

    /// <summary>
    /// The hardest window: the broker receives and accepts the message, but the client never sees the confirm
    /// and the connection dies. The publish must not be reported as failed-and-forgotten nor as confirmed; the
    /// Outbox keeps it, and the retry publishes the SAME MessageId again (at-least-once, a duplicate).
    /// </summary>
    [Fact]
    public async Task A_message_the_broker_accepted_but_whose_confirm_was_lost_is_unknown_and_republished_with_the_same_id()
    {
        await using var probe = await ProbeAsync();
        await using var proxy = new TcpFaultProxy(broker.Host, broker.Port).Start();
        await using var lossyBus = await CreateBusAsync(proxy.ConnectionString);
        await using var healthyBus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "confirm lost" };
        await Pipeline(store).Publish(evt, null, null);
        try
        {
            // Warm up: the exchange is declared through this connection while the broker still answers.
            await lossyBus.Publish(new ProbeEvent { Payload = "warm-up" });
            await probe.DrainMessageIdsAsync(queue);

            proxy.DropServerToClient(true);
            var attempt = Pass(Dispatcher(store, lossyBus));

            // The broker has the message even though the client is still waiting to hear about it.
            await WaitUntilAsync(async () => await probe.CountAsync(queue) == 1u);
            proxy.SeverConnections();
            var result = await attempt;

            Assert.Equal(0, result.Published);
            Assert.Equal(1, result.ConfirmationUnknown);
            Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);

            // Recovery: the same message is published again, and now confirmed. The broker holds both copies.
            var recovered = await Pass(Dispatcher(store, healthyBus));
            Assert.Equal(1, recovered.Published);
            Assert.Equal([evt.MessageId, evt.MessageId], await probe.DrainMessageIdsAsync(queue));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    [Fact]
    public async Task A_confirm_that_never_arrives_times_out_as_unknown_and_the_bus_recovers()
    {
        await using var proxy = new TcpFaultProxy(broker.Host, broker.Port).Start();
        await using var bus = await CreateBusAsync(proxy.ConnectionString, o => o.PublisherConfirmTimeout = TimeSpan.FromSeconds(2));

        await bus.Publish(new ProbeEvent { Payload = "warm-up" });   // declares the exchange while replies still flow

        proxy.DropServerToClient(true);
        var exception = await Assert.ThrowsAsync<RabbitMqPublishOutcomeUnknownException>(() => bus.Publish(new ProbeEvent()));
        Assert.Contains("no confirmation arrived", exception.Message);

        // The stalled channel was discarded, so the next publish is not confused by the outstanding confirm.
        proxy.DropServerToClient(false);
        proxy.SeverConnections();
        await PublishEventuallyAsync(bus, new ProbeEvent { Payload = "after timeout" });
    }

    /// <summary>
    /// Publishes, retrying while the client recovers its connection. Each failed attempt during recovery is a
    /// failure with nothing sent, which is exactly what the Outbox retries; only the final success counts.
    /// </summary>
    private static async Task PublishEventuallyAsync(RabbitMqEventBus bus, ProbeEvent evt, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try
            {
                await bus.Publish(evt);
                return;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    [Fact]
    public async Task Cancelling_a_publish_that_is_waiting_for_its_confirm_never_reports_it_confirmed()
    {
        await using var proxy = new TcpFaultProxy(broker.Host, broker.Port).Start();
        await using var bus = await CreateBusAsync(proxy.ConnectionString);
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "cancelled" };
        await Pipeline(store).Publish(evt, null, null);
        using var cancellation = new CancellationTokenSource();

        await bus.Publish(new ProbeEvent { Payload = "warm-up" });   // declares the exchange while replies still flow
        proxy.DropServerToClient(true);
        var attempt = Dispatcher(store, bus).DispatchPendingBatchAsync(50, cancellation.Token, 5, TimeSpan.Zero);
        await Task.Delay(500);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        var row = (await store.GetByMessageIdAsync(evt.MessageId))!;
        Assert.NotEqual(OutboxMessageStatus.Published, row.Status);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, row.Status);
    }

    // --- Confirms can be turned off -------------------------------------------------------------------

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
        // Legacy behavior: no mandatory flag, so an unroutable command is dropped without an error.
        await bus.Send(new OrphanCommand());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.SendConfirmed(new OrphanCommand(), null, null));
    }

    // --- The final-attempt crash recovery still works with a confirming transport -----------------------

    [Fact]
    public async Task A_row_left_in_doubt_by_a_dead_worker_is_recovered_and_confirmed()
    {
        await using var probe = await ProbeAsync();
        await using var bus = await CreateBusAsync();
        var queue = NewQueue();
        await probe.BindQueueAsync(queue, Exchange<ProbeEvent>(), ExchangeType.Fanout, string.Empty);
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "in doubt" };
        await Pipeline(store).Publish(evt, null, null);
        // The footprint of a worker that died on the final attempt: Publishing, RetryCount at the cap.
        await store.ClaimPendingBatchAsync(10, default, 1, TimeSpan.Zero);
        await store.MarkPublishingAsync(evt.MessageId);
        try
        {
            var result = await Dispatcher(store, bus).DispatchPendingBatchAsync(50, default, 1, TimeSpan.Zero);

            Assert.Equal(1, result.Published);
            Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
            Assert.Equal([evt.MessageId], await probe.DrainMessageIdsAsync(queue));
        }
        finally
        {
            await probe.DeleteQueueAsync(queue);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException("condition was not met in time");
    }
}
