// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Extensions.Configurations;
using Lycia.Extensions.Eventing;
using Lycia.Extensions.Serialization;
using Lycia.Helpers;
using Lycia.Observability;
using Lycia.Outbox;
using Lycia.Persistence.InMemory;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.IntegrationTests;

/// <summary>
/// Publisher confirms across a broker restart. These tests stop and start the broker application, so they own
/// their broker (a separate fixture instance) and never share it with another test class.
/// </summary>
public class RabbitMqPublisherConfirmsRecoveryIntegrationTests(RabbitMqBrokerFixture broker) : IClassFixture<RabbitMqBrokerFixture>
{
    private static readonly NewtonsoftJsonMessageSerializer Serializer = new();

    private async Task<RabbitMqEventBus> CreateBusAsync() =>
        await RabbitMqEventBus.CreateAsync(NullLogger<RabbitMqEventBus>.Instance, new Dictionary<string, (Type, Type)>(),
            new EventBusOptions { ApplicationId = "RecoveryTest", ConnectionString = broker.ConnectionString }, Serializer);

    private static OutboxDispatcher Dispatcher(IOutboxStore store, IEventBus bus) =>
        new(store, bus, Serializer, new LyciaActivitySourceHolder(), NullLogger<OutboxDispatcher>.Instance);

    private static string CommandKey<T>() => MessagingNamingHelper.GetCommandRoutingKey(typeof(T));

    private static string Exchange<T>() => MessagingNamingHelper.GetExchangeName(typeof(T));

    /// <summary>
    /// After the broker restarts and the client recovers, publishes must still be confirmed AND mandatory
    /// unroutable messages must still be returned. The second part is what proves confirmation tracking is
    /// still active on the channel the client recovered or recreated: without it a return is silently ignored.
    /// </summary>
    [Fact]
    public async Task Publisher_confirms_are_still_enforced_after_the_broker_restarts()
    {
        await using var bus = await CreateBusAsync();

        // Before the restart an unroutable command is returned, which is the baseline being preserved.
        await Assert.ThrowsAsync<RabbitMqUnroutableMessageException>(() => bus.Send(new ProbeCommandForRecovery.Orphan()));

        await broker.RestartBrokerAsync();

        var deadline = DateTime.UtcNow.AddSeconds(90);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await bus.Send(new ProbeCommandForRecovery.Orphan());
                last = new InvalidOperationException("an unroutable command was accepted: confirmation tracking was lost");
            }
            catch (RabbitMqUnroutableMessageException)
            {
                return;   // the recovered publish channel still tracks confirms and returns mandatory messages
            }
            catch (Exception ex)
            {
                last = ex;   // the client is still recovering
            }

            await Task.Delay(500);
        }

        throw new Xunit.Sdk.XunitException($"confirmation behavior was not restored after the restart: {last}");
    }

    [Fact]
    public async Task An_outbox_message_survives_a_broker_outage_and_ends_Published_with_the_same_id()
    {
        await using var bus = await CreateBusAsync();
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "outage" };
        await new OutboxOutgoingMessagePipeline(store, Serializer).Publish(evt, null, null);
        var dispatcher = Dispatcher(store, bus);

        // A healthy warm-up publish so the connection is established and the exchange exists.
        await bus.Publish(new ProbeEvent { Payload = "warm-up" });

        await broker.StopBrokerAppAsync();
        try
        {
            var during = await dispatcher.DispatchPendingBatchAsync(50, default, 5, TimeSpan.Zero);
            Assert.Equal(0, during.Published);
            Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
        }
        finally
        {
            await broker.StartBrokerAppAsync();
        }

        // The Outbox retries after the recovery window; here the window is zero. The client needs a moment to
        // reconnect, so keep dispatching until the message is confirmed.
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while ((await store.GetByMessageIdAsync(evt.MessageId))!.Status != OutboxMessageStatus.Published)
        {
            Assert.True(DateTime.UtcNow < deadline, "the message was not published after the broker came back");
            await Task.Delay(500);
            await dispatcher.DispatchPendingBatchAsync(50, default, 50, TimeSpan.Zero);
        }

        Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(evt.MessageId))!.Status);
    }
}

/// <summary>A command type reserved for the restart test, so its exchange is never bound.</summary>
public static class ProbeCommandForRecovery
{
    public interface IRecoveryOrphanServiceCommand : Lycia.Saga.Abstractions.Messaging.ICommandEndpoint;

    public sealed class Orphan : Lycia.Saga.Messaging.CommandBase, IRecoveryOrphanServiceCommand;
}
