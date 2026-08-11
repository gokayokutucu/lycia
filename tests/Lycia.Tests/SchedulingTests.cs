// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0

using System.Runtime.CompilerServices;
using Lycia.Common.Messaging;
using Lycia.Extensions.Helpers;
using Lycia.Extensions.Kafka;
using Lycia.Extensions.Nats;
using Lycia.Extensions.Serialization;
using Lycia.Outbox;
using Lycia.Persistence.InMemory;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Abstractions.Scheduling;
using Lycia.Saga.Messaging;
using Lycia.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lycia.Tests;

public sealed class SchedulingTests
{
    public static IEnumerable<object[]> DelayBuckets()
    {
        yield return [ScheduleDelay.FiveSeconds, TimeSpan.FromSeconds(5), "5s"];
        yield return [ScheduleDelay.ThirtySeconds, TimeSpan.FromSeconds(30), "30s"];
        yield return [ScheduleDelay.OneMinute, TimeSpan.FromMinutes(1), "1m"];
        yield return [ScheduleDelay.FiveMinutes, TimeSpan.FromMinutes(5), "5m"];
        yield return [ScheduleDelay.FifteenMinutes, TimeSpan.FromMinutes(15), "15m"];
        yield return [ScheduleDelay.ThirtyMinutes, TimeSpan.FromMinutes(30), "30m"];
        yield return [ScheduleDelay.OneHour, TimeSpan.FromHours(1), "1h"];
        yield return [ScheduleDelay.SixHours, TimeSpan.FromHours(6), "6h"];
        yield return [ScheduleDelay.TwelveHours, TimeSpan.FromHours(12), "12h"];
        yield return [ScheduleDelay.OneDay, TimeSpan.FromDays(1), "1d"];
        yield return [ScheduleDelay.OneWeek, TimeSpan.FromDays(7), "1w"];
        yield return [ScheduleDelay.OneMonth, TimeSpan.FromDays(30), "1mo"];
        yield return [ScheduleDelay.TwoMonths, TimeSpan.FromDays(60), "2mo"];
        yield return [ScheduleDelay.ThreeMonths, TimeSpan.FromDays(90), "3mo"];
        yield return [ScheduleDelay.FourMonths, TimeSpan.FromDays(120), "4mo"];
        yield return [ScheduleDelay.FiveMonths, TimeSpan.FromDays(150), "5mo"];
        yield return [ScheduleDelay.SixMonths, TimeSpan.FromDays(180), "6mo"];
        yield return [ScheduleDelay.SevenMonths, TimeSpan.FromDays(210), "7mo"];
        yield return [ScheduleDelay.EightMonths, TimeSpan.FromDays(240), "8mo"];
        yield return [ScheduleDelay.NineMonths, TimeSpan.FromDays(270), "9mo"];
        yield return [ScheduleDelay.TenMonths, TimeSpan.FromDays(300), "10mo"];
        yield return [ScheduleDelay.ElevenMonths, TimeSpan.FromDays(330), "11mo"];
        yield return [ScheduleDelay.OneYear, TimeSpan.FromDays(365), "1y"];
    }

    [Theory]
    [MemberData(nameof(DelayBuckets))]
    public void Every_predefined_delay_has_a_stable_duration_and_suffix(
        ScheduleDelay delay, TimeSpan expectedDuration, string expectedSuffix)
    {
        Assert.Equal(expectedDuration, ScheduleDelayResolver.GetDuration(delay));
        Assert.Equal(expectedSuffix, ScheduleDelayResolver.GetSuffix(delay));
    }

    [Fact]
    public async Task Stable_schedule_id_is_idempotent_but_cannot_be_reused_for_another_request()
    {
        var store = new InMemoryScheduleStore();
        var record = Record(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));

        Assert.True((await store.CreateAsync(record)).Created);
        Assert.False((await store.CreateAsync(record)).Created);

        var conflicting = Record(record.DueAtUtc);
        conflicting.ScheduleId = record.ScheduleId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(conflicting));
    }

    [Fact]
    public async Task Expired_claim_is_recovered_and_stale_fencing_token_is_rejected()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var store = new InMemoryScheduleStore();
        var record = Record(now);
        await store.CreateAsync(record);

        var first = Assert.Single(await store.ClaimDueAsync(now, 1, "worker-a", TimeSpan.FromSeconds(10)));
        var second = Assert.Single(await store.ClaimDueAsync(now.AddSeconds(11), 1, "worker-b", TimeSpan.FromSeconds(10)));

        Assert.True(second.FencingToken > first.FencingToken);
        Assert.False(await store.MarkDispatchingAsync(record.ScheduleId, first.LeaseOwner, first.FencingToken));
        Assert.True(await store.MarkDispatchingAsync(record.ScheduleId, second.LeaseOwner, second.FencingToken));
        Assert.True(await store.CompleteAsync(record.ScheduleId, second.LeaseOwner, second.FencingToken, now.AddSeconds(12)));
        Assert.Equal(ScheduleStatus.Completed, (await store.GetAsync(record.ScheduleId))!.Status);
    }

    [Fact]
    public async Task Scheduler_worker_dispatches_only_due_records_and_preserves_message_identity()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var clock = new ManualSchedulingClock(now);
        var store = new InMemoryScheduleStore();
        var due = Record(now);
        var future = Record(now.AddMinutes(1));
        await store.CreateAsync(due);
        await store.CreateAsync(future);
        var dispatcher = new RecordingDispatcher();
        var worker = new SchedulerWorker(store, dispatcher, clock, Options.Create(new SchedulingOptions()),
            NullLogger<SchedulerWorker>.Instance);

        Assert.Equal(1, await worker.RunOnceAsync());
        Assert.Equal(due.MessageId, Assert.Single(dispatcher.Records).MessageId);
        Assert.Equal(ScheduleStatus.Completed, (await store.GetAsync(due.ScheduleId))!.Status);
        Assert.Equal(ScheduleStatus.Pending, (await store.GetAsync(future.ScheduleId))!.Status);
    }

    [Fact]
    public async Task Cancellation_is_idempotent_and_rescheduling_does_not_duplicate_dispatch()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var store = new InMemoryScheduleStore();
        var cancelled = Record(now.AddMinutes(1));
        await store.CreateAsync(cancelled);
        Assert.True(await store.CancelAsync(cancelled.ScheduleId));
        Assert.True(await store.CancelAsync(cancelled.ScheduleId));
        Assert.Empty(await store.ClaimDueAsync(now.AddHours(1), 10, "worker", TimeSpan.FromSeconds(30)));

        var rescheduled = Record(now.AddMinutes(10));
        await store.CreateAsync(rescheduled);
        Assert.True(await store.RescheduleAsync(rescheduled.ScheduleId, now.AddSeconds(5)));
        Assert.Empty(await store.ClaimDueAsync(now.AddSeconds(4), 10, "worker", TimeSpan.FromSeconds(30)));
        Assert.Single(await store.ClaimDueAsync(now.AddSeconds(5), 10, "worker", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Failed_dispatch_retries_after_backoff_and_then_completes()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var clock = new ManualSchedulingClock(now);
        var store = new InMemoryScheduleStore();
        var record = Record(now);
        await store.CreateAsync(record);
        var dispatcher = new FailOnceDispatcher();
        var options = new SchedulingOptions();
        options.Worker.RetryBackoff = TimeSpan.FromSeconds(5);
        var worker = new SchedulerWorker(store, dispatcher, clock, Options.Create(options),
            NullLogger<SchedulerWorker>.Instance);

        Assert.Equal(1, await worker.RunOnceAsync());
        Assert.Equal(ScheduleStatus.RetryPending, (await store.GetAsync(record.ScheduleId))!.Status);
        Assert.Equal(0, await worker.RunOnceAsync());
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await worker.RunOnceAsync());
        Assert.Equal(ScheduleStatus.Completed, (await store.GetAsync(record.ScheduleId))!.Status);
        Assert.Equal(2, dispatcher.Attempts);
    }

    [Fact]
    public async Task Durable_scheduler_restores_command_event_and_targeted_response_semantics()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var clock = new ManualSchedulingClock(now);
        var store = new InMemoryScheduleStore();
        var bus = new RecordingEventBus();
        var serializer = new NewtonsoftJsonMessageSerializer();
        var scheduler = new MessageScheduler(store, serializer, bus, clock,
            Options.Create(new SchedulingOptions { PreferNativeTransportScheduling = false }),
            new InMemorySchedulingResourceRegistry(), NullLogger<MessageScheduler>.Instance);
        var current = new TestCommand { ResponseEndpoint = "Requester Service" };
        var sagaId = Guid.NewGuid();

        var command = new TestCommand();
        var stableCommandScheduleId = Guid.NewGuid();
        var commandScheduleId = await scheduler.ScheduleAsync(command, current, typeof(SchedulingTests), sagaId,
            ScheduleDelay.FiveSeconds, stableCommandScheduleId);
        var originalDueAt = (await store.GetAsync(commandScheduleId))!.DueAtUtc;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(commandScheduleId, await scheduler.ScheduleAsync(command, current, typeof(SchedulingTests), sagaId,
            ScheduleDelay.FiveSeconds, stableCommandScheduleId));
        Assert.Equal(originalDueAt, (await store.GetAsync(commandScheduleId))!.DueAtUtc);
        var @event = new TestEvent();
        var eventScheduleId = await scheduler.ScheduleAsync(@event, current, typeof(SchedulingTests), sagaId,
            ScheduleDelay.FiveSeconds, Guid.NewGuid());
        var response = new TestResponse();
        var responseScheduleId = await scheduler.ScheduleAsync(response, current, typeof(SchedulingTests), sagaId,
            ScheduleDelay.FiveSeconds, Guid.NewGuid());

        Assert.NotEqual(commandScheduleId, command.MessageId);
        Assert.Equal(current.MessageId, command.CausationId);
        Assert.Equal(current.MessageId, command.ParentMessageId);
        Assert.Equal(current.CorrelationId, command.CorrelationId);
        Assert.Equal(sagaId, command.SagaId);
        Assert.Equal(current.MessageId, response.RequestId);
        Assert.Equal("requesterservice", response.ResponseEndpoint);

        var dispatcher = new EventBusSchedulingDispatcher(bus, serializer);
        await dispatcher.DispatchAsync((await store.GetAsync(commandScheduleId))!);
        await dispatcher.DispatchAsync((await store.GetAsync(eventScheduleId))!);
        await dispatcher.DispatchAsync((await store.GetAsync(responseScheduleId))!);

        Assert.Equal(command.MessageId, Assert.Single(bus.Commands).MessageId);
        Assert.Equal(@event.MessageId, Assert.Single(bus.Events).MessageId);
        var targeted = Assert.Single(bus.Responses);
        Assert.Equal(current.MessageId, targeted.Request.MessageId);
        Assert.Equal(response.MessageId, targeted.Response.MessageId);
        Assert.Equal("requesterservice", ((IRequestRoutingMetadata)targeted.Response).ResponseEndpoint);
    }

    [Fact]
    public async Task Due_schedule_hands_off_once_to_the_configured_outbox_pipeline()
    {
        var serializer = new NewtonsoftJsonMessageSerializer();
        var outbox = new InMemoryOutboxStore();
        var pipeline = new OutboxOutgoingMessagePipeline(outbox, serializer);
        var command = new TestCommand();
        var (_, context) = serializer.CreateContextFor(typeof(TestCommand));
        var (body, headers) = serializer.Serialize(command, context);
        var record = new ScheduleRecord
        {
            ScheduleId = Guid.NewGuid(),
            MessageId = command.MessageId,
            MessageType = typeof(TestCommand).AssemblyQualifiedName!,
            MessageKind = ScheduledMessageKind.Command,
            Destination = "command.stockservice",
            DueAtUtc = DateTimeOffset.UtcNow,
            ScheduledAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = ScheduleStatus.Pending,
            Payload = body,
            Headers = headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

        await new EventBusSchedulingDispatcher(pipeline, serializer).DispatchAsync(record);

        var captured = await outbox.GetByMessageIdAsync(command.MessageId);
        Assert.NotNull(captured);
        var envelope = Newtonsoft.Json.JsonConvert.DeserializeObject<OutboxEnvelope>(captured!.Payload);
        Assert.Equal(OutboxOperationKind.Send, envelope!.Operation);
        Assert.Equal(command.MessageId, envelope.MessageId);
    }

    [Fact]
    public void Dynamic_vacuum_requires_proven_ownership_age_inactivity_and_empty_unused_state()
    {
        var now = DateTimeOffset.Parse("2030-01-10T00:00:00Z");
        var options = new SchedulingResourceVacuumOptions
        {
            MinimumResourceAge = TimeSpan.FromDays(1),
            DynamicResourceRetention = TimeSpan.FromDays(2)
        };
        var resource = new SchedulingResourceRecord
        {
            ResourceId = "dynamic-delay",
            IsDynamic = true,
            ManagementMode = SchedulingResourceManagementMode.DynamicScheduling,
            CreatedAtUtc = now.AddDays(-5),
            LastUsedAtUtc = now.AddDays(-3)
        };
        var state = new SchedulingResourceState { Exists = true, OwnershipProven = true };

        Assert.True(SchedulingVacuumEvaluator.Evaluate(resource, state, now, options, 0).Eligible);
        state.MessageCount = 1;
        Assert.Equal(VacuumDecisionReason.HasMessages,
            SchedulingVacuumEvaluator.Evaluate(resource, state, now, options, 0).Reason);
        state.MessageCount = 0;
        state.OwnershipProven = false;
        Assert.Equal(VacuumDecisionReason.UnknownOwnership,
            SchedulingVacuumEvaluator.Evaluate(resource, state, now, options, 0).Reason);
    }

    [Fact]
    public void Ordinary_topology_never_deletes_on_inactivity_alone_and_requires_quarantine_and_opt_in()
    {
        var now = DateTimeOffset.Parse("2030-02-01T00:00:00Z");
        var options = new ApplicationTopologyVacuumOptions
        {
            Mode = VacuumMode.ReportOnly,
            OrphanThreshold = TimeSpan.FromDays(30),
            QuarantinePeriod = TimeSpan.FromDays(14)
        };
        var resource = new SchedulingResourceRecord
        {
            ManagementMode = SchedulingResourceManagementMode.LyciaManaged,
            LastUsedAtUtc = now.AddDays(-60)
        };
        var state = new SchedulingResourceState { Exists = true, OwnershipProven = true };

        var first = ApplicationTopologyOrphanEvaluator.Evaluate(resource, state, now, options, 0);
        Assert.Equal(VacuumDecisionReason.QuarantineIncomplete, first.Reason);
        Assert.False(first.Eligible);
        var reportOnly = ApplicationTopologyOrphanEvaluator.Evaluate(resource, state, now.AddDays(15), options, 0);
        Assert.Equal(VacuumDecisionReason.PolicyPreventsDeletion, reportOnly.Reason);
        options.Mode = VacuumMode.Automatic;
        options.AllowDestructiveApplicationTopologyCleanup = true;
        Assert.True(ApplicationTopologyOrphanEvaluator.Evaluate(resource, state, now.AddDays(15), options, 0).Eligible);
    }

    [Fact]
    public void Rabbit_dynamic_queue_arguments_use_only_supported_broker_features()
    {
        var arguments = RabbitMqSchedulingTopology.CreateQueueArguments(5000, "command.TestCommand", "stock-service", false);

        Assert.Equal(5000L, arguments["x-message-ttl"]);
        Assert.Equal("command.TestCommand", arguments["x-dead-letter-exchange"]);
        Assert.Equal("stock-service", arguments["x-dead-letter-routing-key"]);
        Assert.True(arguments.ContainsKey("x-expires"));
        Assert.DoesNotContain(arguments.Keys, key => key.StartsWith("x-lycia-", StringComparison.Ordinal));
    }

    [Fact]
    public void Kafka_has_no_fake_native_delay_strategy_and_nats_native_only_fails_fast()
    {
        Assert.False(typeof(INativeSchedulingTransport).IsAssignableFrom(typeof(KafkaEventBus)));
        var options = new NatsEventBusOptions
        {
            Url = "nats://127.0.0.1:4222",
            ApplicationId = "TestApplication",
            SchedulingMode = NatsSchedulingMode.NativeOnly
        };

        var error = Assert.Throws<NotSupportedException>(() => new NatsEventBus(
            new Dictionary<string, (Type, Type)>(), options, new NewtonsoftJsonMessageSerializer()));
        Assert.Contains("validated NATS 2.11 baseline", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topology_manifest_skips_generic_messages_without_disabling_supported_heartbeats()
    {
        var resources = new InMemorySchedulingResourceRegistry();
        var manifests = new InMemoryTopologyManifestRegistry();
        var topology = new Dictionary<string, (Type MessageType, Type HandlerType)>
        {
            ["message.generic"] = (typeof(Messages.DummyEvent), typeof(SchedulingTests)),
            ["command.supported"] = (typeof(TestCommand), typeof(SchedulingTests))
        };
        var worker = new TopologyManifestWorker(manifests, resources, new RecordingEventBus(), topology,
            new ManualSchedulingClock(DateTimeOffset.Parse("2030-01-01T00:00:00Z")),
            Options.Create(new SchedulingOptions()), NullLogger<TopologyManifestWorker>.Instance);

        var manifest = await worker.HeartbeatOnceAsync();

        Assert.DoesNotContain("message.generic", manifest.OwnedResources);
        Assert.Contains("command.supported", manifest.OwnedResources);
        Assert.Null(await resources.GetAsync("message.generic"));
        Assert.Equal(ScheduledMessageKind.Command,
            (await resources.GetAsync("command.supported"))!.MessageKind);
    }

    private static ScheduleRecord Record(DateTimeOffset dueAtUtc) => new()
    {
        ScheduleId = Guid.NewGuid(),
        MessageId = Guid.NewGuid(),
        MessageType = typeof(TestEvent).AssemblyQualifiedName!,
        MessageKind = ScheduledMessageKind.Event,
        Destination = "event.TestEvent",
        DueAtUtc = dueAtUtc,
        ScheduledAtUtc = dueAtUtc.AddMinutes(-1),
        Status = ScheduleStatus.Pending,
        Payload = [1]
    };

    public interface IStockServiceCommand : ICommand, ICommandEndpoint;

    public sealed class TestCommand : CommandBase, IStockServiceCommand;

    public sealed class TestEvent : EventBase;

    public sealed class TestResponse : ResponseBase<TestCommand>;

    private sealed class RecordingDispatcher : ISchedulingDispatcher
    {
        public List<ScheduleRecord> Records { get; } = [];

        public Task DispatchAsync(ScheduleRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceDispatcher : ISchedulingDispatcher
    {
        public int Attempts { get; private set; }

        public Task DispatchAsync(ScheduleRecord record, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Attempts == 1
                ? Task.FromException(new InvalidOperationException("Transient transport failure."))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public string ApplicationId => "Requester Service";
        public List<IMessage> Commands { get; } = [];
        public List<IMessage> Events { get; } = [];
        public List<(IMessage Request, IResponse Response)> Responses { get; } = [];

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest>
        {
            Responses.Add((request, response));
            return Task.CompletedTask;
        }

        public Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<IncomingMessage> ConsumeWithAckAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
