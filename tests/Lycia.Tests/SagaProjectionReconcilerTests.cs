using Lycia.Extensions.SplitStore;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;

namespace Lycia.Tests;

public sealed class SagaProjectionReconcilerTests
{
    [Theory]
    [InlineData(ProjectionApplyOutcome.Applied, ReconciliationStatus.Applied, 1, 0)]
    [InlineData(ProjectionApplyOutcome.AlreadyApplied, ReconciliationStatus.Applied, 1, 0)]
    [InlineData(ProjectionApplyOutcome.Superseded, ReconciliationStatus.Superseded, 0, 1)]
    public async Task RunOnce_classifies_successful_projection_outcomes(
        ProjectionApplyOutcome outcome, ReconciliationStatus status, int applied, int superseded)
    {
        var intent = Intent();
        var (reconciler, canonical, operational) = Create(intent);
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var result = await reconciler.RunOnceAsync();

        Assert.Equal(1, result.Claimed);
        Assert.Equal(applied, result.Applied);
        Assert.Equal(superseded, result.Superseded);
        canonical.Verify(x => x.MarkCompletedAsync(intent.TransitionId, status, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunOnce_marks_version_conflicts_terminal()
    {
        var intent = Intent();
        var (reconciler, canonical, operational) = Create(intent);
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectionApplyOutcome.VersionConflict);

        var result = await reconciler.RunOnceAsync();

        Assert.Equal(1, result.Failed);
        canonical.Verify(x => x.MarkFailedAsync(intent.TransitionId, "VersionConflict",
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunOnce_retries_transient_failures_with_bounded_backoff()
    {
        var intent = Intent(attemptCount: 1);
        var (reconciler, canonical, operational) = Create(intent);
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var result = await reconciler.RunOnceAsync();

        Assert.Equal(1, result.Retried);
        canonical.Verify(x => x.MarkRetryAsync(intent.TransitionId, It.IsAny<DateTime>(), "TimeoutException",
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunOnce_marks_exhausted_attempts_terminal()
    {
        var intent = Intent(attemptCount: 3);
        var (reconciler, canonical, operational) = Create(intent, maxAttempts: 3);
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var result = await reconciler.RunOnceAsync();

        Assert.Equal(1, result.Failed);
        canonical.Verify(x => x.MarkFailedAsync(intent.TransitionId, "AttemptsExhausted",
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunOnce_marks_malformed_payload_terminal()
    {
        var intent = Intent();
        var (reconciler, canonical, operational) = Create(intent);
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JsonSerializationException("invalid"));

        var result = await reconciler.RunOnceAsync();

        Assert.Equal(1, result.Failed);
        canonical.Verify(x => x.MarkFailedAsync(intent.TransitionId, "MalformedPayload",
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunOnce_propagates_cancellation_without_reclassifying_the_intent()
    {
        var intent = Intent();
        var (reconciler, canonical, operational) = Create(intent);
        using var source = new CancellationTokenSource();
        source.Cancel();
        operational.Setup(x => x.ApplyAsync(intent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(source.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconciler.RunOnceAsync(source.Token));
        canonical.Verify(x => x.MarkRetryAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        canonical.Verify(x => x.MarkFailedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (SagaProjectionReconciler Reconciler, Mock<IReconciliationStore> Canonical,
        Mock<IOperationalSagaProjectionStore> Operational) Create(SagaProjectionIntent intent, int maxAttempts = 3)
    {
        var canonical = new Mock<IReconciliationStore>();
        canonical.Setup(x => x.ClaimAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync([intent]);
        var operational = new Mock<IOperationalSagaProjectionStore>();
        var options = Options.Create(new ReconciliationWorkerOptions
        {
            MaxAttempts = maxAttempts,
            RetryBackoff = TimeSpan.Zero,
            MaxRetryBackoff = TimeSpan.Zero,
            MaxJitter = TimeSpan.Zero
        });
        return (new SagaProjectionReconciler(canonical.Object, operational.Object, options,
            NullLogger<SagaProjectionReconciler>.Instance), canonical, operational);
    }

    private static SagaProjectionIntent Intent(int attemptCount = 1) => new()
    {
        TransitionId = Guid.NewGuid(),
        SagaId = Guid.NewGuid(),
        TargetVersion = 2,
        AttemptCount = attemptCount,
        Payload = "{}"
    };
}
