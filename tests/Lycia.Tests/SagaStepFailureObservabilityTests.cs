// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Diagnostics;
using Lycia.Common.SagaSteps;
using Lycia.Compensating;
using Lycia.Extensions.Serialization;
using Lycia.Middleware;
using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Serializers;
using Lycia.Stores;
using Lycia.Tests.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ISagaIdGenerator = Lycia.Saga.Abstractions.ISagaIdGenerator;

namespace Lycia.Tests;

/// <summary>
/// The saga handler base classes catch a business exception and record it as a failed step instead of
/// letting it propagate, so the dispatch returns normally. The failure must still be visible to
/// operators in logs and traces, not only as a durable step record.
/// </summary>
public class SagaStepFailureObservabilityTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class NoHandler;

    [Fact]
    public async Task A_failed_step_is_logged_as_a_warning_and_marks_the_current_span_as_an_error()
    {
        var logger = new CapturingLogger<SagaCompensationCoordinator>();
        var services = new ServiceCollection();
        services.AddSingleton<IMessageSerializer, NewtonsoftJsonMessageSerializer>();
        var eventBus = Mock.Of<IEventBus>();
        var sagaIdGen = Mock.Of<ISagaIdGenerator>();
        services.AddSingleton<ISagaStore>(new InMemorySagaStore(eventBus, sagaIdGen, Mock.Of<ISagaCompensationCoordinator>()));
        services.AddSingleton(eventBus);
        services.AddSingleton<ILogger<SagaCompensationCoordinator>>(logger);
        var provider = services.BuildServiceProvider();
        var coordinator = new SagaCompensationCoordinator(provider, sagaIdGen, provider.GetRequiredService<IMessageSerializer>());

        var sourceName = $"Lycia.Tests.{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var failure = new InvalidOperationException("Injected inventory failure.");
        var info = new SagaStepFailureInfo("Saga step failed", failure.GetType().Name, failure.ToString());
        var message = new DummyEvent { MessageId = Guid.NewGuid(), ParentMessageId = Guid.Empty };
        var sagaId = Guid.NewGuid();

        using (var activity = source.StartActivity("handler"))
        {
            await coordinator.CompensateAsync(sagaId, typeof(DummyEvent), typeof(NoHandler), message, info);

            Assert.Equal(ActivityStatusCode.Error, activity!.Status);
            Assert.Equal("Failed", activity.GetTagItem("lycia.saga.step.status"));
            Assert.Equal(nameof(InvalidOperationException), activity.GetTagItem("exception.type"));
            Assert.Equal("System.InvalidOperationException: Injected inventory failure.",
                activity.GetTagItem("exception.message"));
        }

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Injected inventory failure.", warning.Message);
        Assert.Contains(sagaId.ToString(), warning.Message);

        // Idempotent: a repeated failure report for the same step is not logged again.
        await coordinator.CompensateAsync(sagaId, typeof(DummyEvent), typeof(NoHandler), message, info);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task The_tracing_middleware_does_not_overwrite_a_recorded_failure_with_Completed()
    {
        Activity.Current = null;
        var sourceName = $"Lycia.Tests.{Guid.NewGuid():N}";
        using var holder = new LyciaActivitySourceHolder(new ActivitySource(sourceName));
        var middleware = new ActivityTracingMiddleware(holder, Mock.Of<ISagaContextAccessor>(),
            NullLogger<ActivityTracingMiddleware>.Instance);

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => captured = a
        };
        ActivitySource.AddActivityListener(listener);

        var context = new Mock<IInvocationContext>();
        context.SetupGet(c => c.Message).Returns(new DummyEvent { MessageId = Guid.NewGuid() });
        context.SetupGet(c => c.HandlerType).Returns(typeof(NoHandler));
        context.SetupGet(c => c.SagaId).Returns(Guid.NewGuid());

        await middleware.InvokeAsync(context.Object, () =>
        {
            // What the coordinator does when a handler records a failed step and returns normally.
            Activity.Current!.SetStatus(ActivityStatusCode.Error, "boom");
            Activity.Current.SetTag("lycia.saga.step.status", "Failed");
            return Task.CompletedTask;
        });

        Assert.NotNull(captured);
        Assert.Equal("Failed", captured!.GetTagItem("lycia.saga.step.status"));
        Assert.Equal(ActivityStatusCode.Error, captured.Status);
    }
}
