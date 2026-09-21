// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Common.SagaSteps;
using Lycia.Extensions.Serialization;
using Lycia.Observability;
using Lycia.Outbox;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Abstractions.Outbox;
using Lycia.Saga.Messaging;
using Microsoft.Extensions.Logging;

namespace Lycia.Persistence.InMemory.Tests;

/// <summary>
/// Crash recovery of an Outbox attempt whose outcome was never recorded. <c>MarkPublishingAsync</c> counts
/// an attempt when it starts, so a worker that dies afterwards leaves a <c>Publishing</c> row that may or
/// may not have reached the broker. Such a row must never become invisible to recovery — least of all
/// when it was the final permitted attempt — and recovery must favor at-least-once delivery: publish
/// again, with the same <c>MessageId</c>, rather than assume success or silently drop the message.
/// </summary>
/// <remarks>
/// Process death is modelled with a shared <see cref="ProcessLife"/>: once it is killed, every store
/// and transport call made by the "dead" process throws and changes nothing, exactly like a process that no
/// longer exists. A second dispatcher over the surviving store plays the restarted worker.
/// </remarks>
public class OutboxCrashRecoveryTests
{
    private const int MaxAttempts = 3;

    private sealed class ProbeEvent : EventBase
    {
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class ProcessDiedException : Exception;

    private sealed class ProcessLife
    {
        public bool Dead { get; private set; }
        public void Kill() => Dead = true;
        public void ThrowIfDead() { if (Dead) throw new ProcessDiedException(); }
    }

    public enum DeathPoint
    {
        /// <summary>The worker claimed the row and died before MarkPublishingAsync.</summary>
        AfterClaim,
        /// <summary>The worker died immediately after MarkPublishingAsync, before invoking the transport.</summary>
        AfterMarkPublishing,
        /// <summary>The broker accepted the message but the worker died before persisting any outcome.</summary>
        AfterBrokerAcceptance
    }

    /// <summary>An <see cref="IOutboxStore"/> that stops responding once its process has "died".</summary>
    private sealed class MortalOutboxStore(IOutboxStore inner, ProcessLife life, DeathPoint? dieAt) : IOutboxStore
    {
        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.AddAsync(message, cancellationToken); }

        public Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.GetByMessageIdAsync(messageId, cancellationToken); }

        public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingBatchAsync(int maxCount,
            CancellationToken cancellationToken = default, int maxAttempts = 5, TimeSpan? recoveryTimeout = null)
        {
            life.ThrowIfDead();
            var claimed = await inner.ClaimPendingBatchAsync(maxCount, cancellationToken, maxAttempts, recoveryTimeout);
            if (dieAt == DeathPoint.AfterClaim && claimed.Count > 0) life.Kill();
            return claimed;
        }

        public async Task MarkPublishingAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            life.ThrowIfDead();
            await inner.MarkPublishingAsync(messageId, cancellationToken);
            if (dieAt == DeathPoint.AfterMarkPublishing) life.Kill();
        }

        public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.MarkPublishedAsync(messageId, cancellationToken); }

        public Task MarkConfirmationUnknownAsync(Guid messageId, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.MarkConfirmationUnknownAsync(messageId, cancellationToken); }

        public Task MarkFailedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.MarkFailedAsync(messageId, failureInfo, cancellationToken); }

        public Task MarkAbandonedAsync(Guid messageId, SagaStepFailureInfo? failureInfo, CancellationToken cancellationToken = default)
        { life.ThrowIfDead(); return inner.MarkAbandonedAsync(messageId, failureInfo, cancellationToken); }
    }

    /// <summary>
    /// A transport recording every accepted publish. It is unconfirming by default, like RabbitMQ; a
    /// confirming variant models Kafka/JetStream.
    /// </summary>
    private class RecordingEventBus(ProcessLife life, bool dieAfterAcceptance = false, bool brokerDown = false)
        : IEventBus
    {
        public readonly List<Guid> Accepted = [];
        public string ApplicationId => "TestApp";

        public Task Send<TCommand>(TCommand command, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task Respond<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType = null,
            Guid? sagaId = null, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;

        public virtual Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            life.ThrowIfDead();
            if (brokerDown) throw new TimeoutException("broker unreachable");
            Accepted.Add(@event.MessageId);
            if (dieAfterAcceptance) life.Kill();
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<(byte[] Body, Type MessageType, Type HandlerType,
            IReadOnlyDictionary<string, object?> Headers)> ConsumeAsync(bool autoAck = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<Lycia.Common.Messaging.IncomingMessage> ConsumeWithAckAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ConfirmingEventBus(ProcessLife life) : RecordingEventBus(life), IConfirmedEventBus
    {
        public Task SendConfirmed<TCommand>(TCommand command, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TCommand : ICommand => Task.CompletedTask;

        public Task PublishConfirmed<TEvent>(TEvent message, Type? handlerType, Guid? sagaId,
            CancellationToken cancellationToken = default) where TEvent : IEvent =>
            Publish(message, handlerType, sagaId, cancellationToken);

        public Task RespondConfirmed<TRequest, TResponse>(TRequest request, TResponse response, Type? handlerType,
            Guid? sagaId, CancellationToken cancellationToken = default)
            where TRequest : IMessage where TResponse : IResponse<TRequest> => Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger<OutboxDispatcher>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static OutboxDispatcher Dispatcher(IOutboxStore store, IEventBus bus, CapturingLogger? logger = null) =>
        new(store, bus, new NewtonsoftJsonMessageSerializer(), new LyciaActivitySourceHolder(),
            logger ?? new CapturingLogger());

    /// <summary>One pass of a worker with an immediate recovery window (InMemory staleness is clock based).</summary>
    private static Task<OutboxDispatchResult> Pass(OutboxDispatcher dispatcher, int maxAttempts = MaxAttempts) =>
        dispatcher.DispatchPendingBatchAsync(50, default, maxAttempts, TimeSpan.Zero);

    /// <summary>Runs a pass in a process that may die mid-dispatch; the death is expected, not a failure.</summary>
    private static async Task<bool> PassThatMayDie(OutboxDispatcher dispatcher, int maxAttempts = MaxAttempts)
    {
        try { await Pass(dispatcher, maxAttempts); return false; }
        catch (ProcessDiedException) { return true; }
    }

    private static async Task<(InMemoryOutboxStore Store, Guid MessageId)> CaptureMessage()
    {
        var store = new InMemoryOutboxStore();
        var evt = new ProbeEvent { Payload = "business-critical" };
        await new OutboxOutgoingMessagePipeline(store, new NewtonsoftJsonMessageSerializer()).Publish(evt, null, null);
        return (store, evt.MessageId);
    }

    /// <summary>
    /// Drives the row through <paramref name="completedAttempts"/> ordinary unconfirmed attempts, then starts
    /// the next attempt in a process that dies at <paramref name="deathPoint"/>. Returns the surviving store.
    /// </summary>
    private static async Task<(InMemoryOutboxStore Store, Guid MessageId, RecordingEventBus DeadBus)> RowLeftInDoubt(
        int completedAttempts, DeathPoint deathPoint, int maxAttempts = MaxAttempts)
    {
        var (store, id) = await CaptureMessage();
        var healthy = new ProcessLife();
        for (var attempt = 0; attempt < completedAttempts; attempt++)
            await Pass(Dispatcher(store, new RecordingEventBus(healthy)), maxAttempts);

        var life = new ProcessLife();
        var deadBus = new RecordingEventBus(life, dieAfterAcceptance: deathPoint == DeathPoint.AfterBrokerAcceptance);
        var died = await PassThatMayDie(Dispatcher(new MortalOutboxStore(store, life, deathPoint), deadBus), maxAttempts);
        Assert.True(died, "the simulated process was expected to die mid-dispatch");
        return (store, id, deadBus);
    }

    // --- The reported failure --------------------------------------------------------------------------

    [Theory]
    [InlineData(DeathPoint.AfterMarkPublishing)]
    [InlineData(DeathPoint.AfterBrokerAcceptance)]
    public async Task A_worker_death_on_the_final_attempt_does_not_strand_the_message(DeathPoint deathPoint)
    {
        var (store, id, deadBus) = await RowLeftInDoubt(MaxAttempts - 1, deathPoint);

        var stranded = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Publishing, stranded.Status);
        Assert.Equal(MaxAttempts, stranded.RetryCount);

        // A restarted worker, after the recovery window, must pick the row up again.
        var restartedBus = new RecordingEventBus(new ProcessLife());
        var result = await Pass(Dispatcher(store, restartedBus));

        Assert.True(result.Claimed == 1,
            $"the in-doubt final-attempt row was never handed back to a worker (status {stranded.Status}, " +
            $"RetryCount {stranded.RetryCount} of {MaxAttempts}): the message is stranded");
        Assert.Equal([id], restartedBus.Accepted);
        Assert.NotEqual(OutboxMessageStatus.Publishing, (await store.GetByMessageIdAsync(id))!.Status);
    }

    // --- The chosen conservative behavior ---------------------------------------------------------------

    [Theory]
    [InlineData(DeathPoint.AfterMarkPublishing, 0)]
    [InlineData(DeathPoint.AfterBrokerAcceptance, 1)]
    public async Task Recovery_republishes_with_the_same_MessageId_because_acceptance_is_unknowable(
        DeathPoint deathPoint, int acceptedBeforeDeath)
    {
        var (store, id, deadBus) = await RowLeftInDoubt(MaxAttempts - 1, deathPoint);
        Assert.Equal(acceptedBeforeDeath, deadBus.Accepted.Count);

        var recoveryBus = new RecordingEventBus(new ProcessLife());
        await Pass(Dispatcher(store, recoveryBus));

        // Whether or not the first attempt reached the broker, it is published again: at-least-once.
        Assert.Equal([id], recoveryBus.Accepted);
        Assert.All(deadBus.Accepted, accepted => Assert.Equal(id, accepted));

        // An unconfirming transport never yields Published; the attempt is recorded as ConfirmationUnknown.
        var recovered = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, recovered.Status);
        Assert.Equal(MaxAttempts + 1, recovered.RetryCount);
    }

    [Fact]
    public async Task Recovery_through_a_confirming_transport_ends_in_Published()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);

        var bus = new ConfirmingEventBus(new ProcessLife());
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(1, result.Published);
        Assert.Equal(OutboxMessageStatus.Published, (await store.GetByMessageIdAsync(id))!.Status);
        Assert.Equal([id], bus.Accepted);
    }

    [Fact]
    public async Task A_recovered_row_is_published_once_and_then_stays_put()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);
        var bus = new RecordingEventBus(new ProcessLife());
        var dispatcher = Dispatcher(store, bus);

        await Pass(dispatcher);
        for (var pass = 0; pass < 4; pass++)
            Assert.Equal(0, (await Pass(dispatcher)).Claimed);

        Assert.Equal([id], bus.Accepted);
    }

    [Fact]
    public async Task Recovery_is_logged_as_a_warning_naming_the_message()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);
        var logger = new CapturingLogger();

        await Pass(Dispatcher(store, new RecordingEventBus(new ProcessLife()), logger));

        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(id.ToString(), warning.Message);
        Assert.Contains("doubt", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Crash recovery stays bounded and operator-visible ------------------------------------------

    [Fact]
    public async Task A_second_death_during_the_recovery_attempt_ends_in_Abandoned_without_another_publish()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);

        // The recovery attempt itself dies after the broker accepted it.
        var life = new ProcessLife();
        var secondDeadBus = new RecordingEventBus(life, dieAfterAcceptance: true);
        Assert.True(await PassThatMayDie(
            Dispatcher(new MortalOutboxStore(store, life, DeathPoint.AfterBrokerAcceptance), secondDeadBus)));
        Assert.Equal(MaxAttempts + 1, (await store.GetByMessageIdAsync(id))!.RetryCount);

        // Two consecutive workers died on this message: stop publishing and make it operator-visible.
        var bus = new RecordingEventBus(new ProcessLife());
        var logger = new CapturingLogger();
        var result = await Pass(Dispatcher(store, bus, logger));

        Assert.Equal(1, result.Abandoned);
        Assert.Empty(bus.Accepted);
        var abandoned = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Abandoned, abandoned.Status);
        Assert.NotNull(abandoned.FailureInfo);
        Assert.Contains("stopped", abandoned.FailureInfo!.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(id.ToString()));

        // Terminal.
        Assert.Equal(0, (await Pass(Dispatcher(store, bus))).Claimed);
    }

    [Fact]
    public async Task A_message_that_kills_every_worker_is_published_at_most_MaxAttempts_plus_one_times()
    {
        var (store, id) = await CaptureMessage();
        var accepted = 0;

        // Every worker accepts the publish and then dies before recording anything.
        for (var restart = 0; restart < 3 * MaxAttempts; restart++)
        {
            var life = new ProcessLife();
            var bus = new RecordingEventBus(life, dieAfterAcceptance: true);
            await PassThatMayDie(Dispatcher(new MortalOutboxStore(store, life, DeathPoint.AfterBrokerAcceptance), bus));
            accepted += bus.Accepted.Count;
        }

        Assert.Equal(MaxAttempts + 1, accepted);
        var row = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Abandoned, row.Status);
        Assert.NotNull(row.FailureInfo);
    }

    [Fact]
    public async Task A_broker_outage_during_the_recovery_attempt_is_operator_visible()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterMarkPublishing);

        var bus = new RecordingEventBus(new ProcessLife(), brokerDown: true);
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(1, result.Abandoned);
        var row = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Abandoned, row.Status);
        Assert.NotNull(row.FailureInfo);
    }

    // --- Earlier crash windows keep their existing behavior ---------------------------------------------

    [Fact]
    public async Task A_death_before_MarkPublishing_costs_no_attempt()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterClaim);

        var claimed = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Claimed, claimed.Status);
        Assert.Equal(MaxAttempts - 1, claimed.RetryCount);

        var bus = new RecordingEventBus(new ProcessLife());
        await Pass(Dispatcher(store, bus));

        Assert.Equal([id], bus.Accepted);
        Assert.Equal(MaxAttempts, (await store.GetByMessageIdAsync(id))!.RetryCount);
    }

    [Fact]
    public async Task A_death_on_an_earlier_attempt_is_an_ordinary_retry()
    {
        var (store, id, _) = await RowLeftInDoubt(0, DeathPoint.AfterBrokerAcceptance);
        Assert.Equal(1, (await store.GetByMessageIdAsync(id))!.RetryCount);

        var bus = new RecordingEventBus(new ProcessLife());
        var logger = new CapturingLogger();
        await Pass(Dispatcher(store, bus, logger));

        Assert.Equal([id], bus.Accepted);
        Assert.Equal(2, (await store.GetByMessageIdAsync(id))!.RetryCount);
        // Not at the cap, so this is normal retry policy and does not warrant the in-doubt warning.
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // --- Shutdown cancellation -----------------------------------------------------------------------------

    /// <summary>A transport call that is interrupted by host shutdown, after the broker may have accepted it.</summary>
    private sealed class ShutdownDuringPublishEventBus(CancellationTokenSource shutdown) : RecordingEventBus(new ProcessLife())
    {
        public override Task Publish<TEvent>(TEvent @event, Type? handlerType = null, Guid? sagaId = null,
            CancellationToken cancellationToken = default)
        {
            Accepted.Add(@event.MessageId);
            shutdown.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private static async Task InterruptedByShutdown(InMemoryOutboxStore store)
    {
        using var shutdown = new CancellationTokenSource();
        var dispatcher = Dispatcher(store, new ShutdownDuringPublishEventBus(shutdown));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchPendingBatchAsync(50, shutdown.Token, MaxAttempts, TimeSpan.Zero));
    }

    [Fact]
    public async Task Shutdown_during_the_final_attempt_leaves_the_row_for_recovery_not_for_an_operator()
    {
        var (store, id) = await CaptureMessage();
        for (var attempt = 0; attempt < MaxAttempts - 1; attempt++)
            await Pass(Dispatcher(store, new RecordingEventBus(new ProcessLife())));

        await InterruptedByShutdown(store);

        // A graceful stop is treated exactly like a crash at this point: nothing is written, no operator
        // action is demanded, and the next worker resolves the in-doubt attempt.
        var interrupted = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Publishing, interrupted.Status);
        Assert.Equal(MaxAttempts, interrupted.RetryCount);

        var bus = new RecordingEventBus(new ProcessLife());
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(0, result.Abandoned);
        Assert.Equal([id], bus.Accepted);
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, (await store.GetByMessageIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Shutdown_during_an_earlier_attempt_still_records_ConfirmationUnknown()
    {
        var (store, id) = await CaptureMessage();

        await InterruptedByShutdown(store);

        var interrupted = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.ConfirmationUnknown, interrupted.Status);
        Assert.Equal(1, interrupted.RetryCount);
    }

    [Fact]
    public async Task Shutdown_during_the_recovery_attempt_is_bounded_and_ends_operator_visible()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);

        await InterruptedByShutdown(store);   // the recovery attempt is interrupted too
        Assert.Equal(MaxAttempts + 1, (await store.GetByMessageIdAsync(id))!.RetryCount);

        var bus = new RecordingEventBus(new ProcessLife());
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(1, result.Abandoned);
        Assert.Empty(bus.Accepted);
        Assert.Equal(OutboxMessageStatus.Abandoned, (await store.GetByMessageIdAsync(id))!.Status);
    }

    // --- Further crash windows and concurrency ------------------------------------------------------------

    [Fact]
    public async Task A_death_between_the_recovery_claim_and_its_attempt_does_not_consume_the_recovery_attempt()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);

        // A worker claims the in-doubt row for recovery and dies before starting the attempt.
        var life = new ProcessLife();
        Assert.True(await PassThatMayDie(
            Dispatcher(new MortalOutboxStore(store, life, DeathPoint.AfterClaim), new RecordingEventBus(life))));
        var claimed = (await store.GetByMessageIdAsync(id))!;
        Assert.Equal(OutboxMessageStatus.Claimed, claimed.Status);
        Assert.Equal(MaxAttempts, claimed.RetryCount);

        // The recovery attempt has not been used, so the next worker still gets to make it.
        var bus = new RecordingEventBus(new ProcessLife());
        await Pass(Dispatcher(store, bus));

        Assert.Equal([id], bus.Accepted);
        Assert.Equal(MaxAttempts + 1, (await store.GetByMessageIdAsync(id))!.RetryCount);
    }

    [Fact]
    public async Task A_death_between_the_claim_and_the_abandon_still_ends_in_Abandoned()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);
        var life = new ProcessLife();
        var secondBus = new RecordingEventBus(life, dieAfterAcceptance: true);
        Assert.True(await PassThatMayDie(
            Dispatcher(new MortalOutboxStore(store, life, DeathPoint.AfterBrokerAcceptance), secondBus)));

        // The next worker claims the lost recovery attempt and dies before it can abandon it.
        var thirdLife = new ProcessLife();
        Assert.True(await PassThatMayDie(
            Dispatcher(new MortalOutboxStore(store, thirdLife, DeathPoint.AfterClaim), new RecordingEventBus(thirdLife))));
        Assert.Equal(OutboxMessageStatus.Claimed, (await store.GetByMessageIdAsync(id))!.Status);

        var bus = new RecordingEventBus(new ProcessLife());
        var result = await Pass(Dispatcher(store, bus));

        Assert.Equal(1, result.Abandoned);
        Assert.Empty(bus.Accepted);
        Assert.Equal(OutboxMessageStatus.Abandoned, (await store.GetByMessageIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Two_workers_racing_for_a_stale_in_doubt_row_publish_it_once()
    {
        var (store, id, _) = await RowLeftInDoubt(MaxAttempts - 1, DeathPoint.AfterBrokerAcceptance);

        // Age the row well past a real recovery window: a zero window would make every claim stale at once.
        var window = TimeSpan.FromMinutes(1);
        (await store.GetByMessageIdAsync(id))!.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5);

        var first = new RecordingEventBus(new ProcessLife());
        var second = new RecordingEventBus(new ProcessLife());
        var firstDispatcher = Dispatcher(store, first);
        var secondDispatcher = Dispatcher(store, second);

        await Task.WhenAll(
            Task.Run(() => firstDispatcher.DispatchPendingBatchAsync(50, default, MaxAttempts, window)),
            Task.Run(() => secondDispatcher.DispatchPendingBatchAsync(50, default, MaxAttempts, window)));

        Assert.Equal(1, first.Accepted.Count + second.Accepted.Count);
        Assert.Equal(MaxAttempts + 1, (await store.GetByMessageIdAsync(id))!.RetryCount);
    }
}
