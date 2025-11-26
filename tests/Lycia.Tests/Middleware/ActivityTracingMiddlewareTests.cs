using System.Diagnostics;
using Lycia.Middleware;
using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Contexts;
using Lycia.Saga.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.Tests.Middleware;

public class ActivityTracingMiddlewareTests
{
    private readonly LyciaActivitySourceHolder _sourceHolder =
        new(new ActivitySource("Lycia"));

    private readonly ISagaContextAccessor _dummyAccessor =
        new DummySagaContextAccessor();

    private readonly ILogger<ActivityTracingMiddleware> _logger =
        NullLogger<ActivityTracingMiddleware>.Instance;

    [Fact]
    public async Task InvokeAsync_Should_Set_Basic_Tags_And_Completed_Status()
    {
        // Arrange
        Activity.Current = null; // start clean

        var middleware = new ActivityTracingMiddleware(_sourceHolder, _dummyAccessor, _logger);

        var sagaId = Guid.NewGuid();
        var message = new FakeInvocationMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ApplicationId = "Sample.Order.Orchestration.Seq.Consumer"
        };

        var context = new FakeInvocationContext
        {
            SagaId = sagaId,
            HandlerType = typeof(FakeSagaHandler),
            Message = message
        };

        // Act
        await middleware.InvokeAsync(context, () => Task.CompletedTask);

        // Assert
        // Middleware disposes the Activity it creates,
        // so capturing the Activity during testing is safer using an ActivityListener.
        // Here, a simple approach: capture the last span via ActivityListener.

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceHolder.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => captured = a
        };

        ActivitySource.AddActivityListener(listener);

        // Run again (this time the listener will see the span)
        await middleware.InvokeAsync(context, () => Task.CompletedTask);

        Assert.NotNull(captured);
        Assert.Equal("Saga.FakeSagaHandler", captured!.DisplayName);

        Assert.Equal(sagaId.ToString(), captured.GetTagItem("lycia.saga.id"));
        Assert.Equal(message.MessageId.ToString(), captured.GetTagItem("lycia.message.id"));
        Assert.Equal(typeof(FakeSagaHandler).FullName, captured.GetTagItem("lycia.handler"));
        Assert.Equal(message.CorrelationId.ToString(), captured.GetTagItem("lycia.correlation.id"));
        Assert.Equal(message.ApplicationId, captured.GetTagItem("lycia.application.id"));
        Assert.Equal("Completed", captured.GetTagItem("lycia.saga.step.status"));
    }

    [Fact]
    public async Task InvokeAsync_When_Handler_Throws_Should_Set_Error_Status_And_Exception_Tags()
    {
        // Arrange
        Activity.Current = null;

        var middleware = new ActivityTracingMiddleware(_sourceHolder, _dummyAccessor, _logger);

        var sagaId = Guid.NewGuid();
        var message = new FakeInvocationMessage
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            ApplicationId = "Sample.Order.Orchestration.Seq.Consumer"
        };

        var context = new FakeInvocationContext
        {
            SagaId = sagaId,
            HandlerType = typeof(FakeSagaHandler),
            Message = message
        };

        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceHolder.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => captured = a
        };
        ActivitySource.AddActivityListener(listener);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, () => throw new InvalidOperationException("boom")));

        Assert.NotNull(captured);
        Assert.Equal(ActivityStatusCode.Error, captured!.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, captured.GetTagItem("exception.type"));
        Assert.Equal("boom", captured.GetTagItem("exception.message"));
        Assert.NotNull(captured.GetTagItem("exception.stacktrace"));
    }

    // --- Fake types for tests ---

    private sealed class DummySagaContextAccessor : ISagaContextAccessor
    {
        public ISagaContext? Current { get; set; }
    }

    private sealed class FakeInvocationContext : IInvocationContext
    {
        public IMessage Message { get; set; }
        public ISagaContext? SagaContext { get; set; }
        public Type HandlerType { get; set; }
        public Guid? SagaId { get; set; }
        public string ApplicationId { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public Exception? LastException { get; set; }
    }

    private sealed class FakeInvocationMessage : IMessage
    {
        public Guid MessageId { get; set; }
        public Guid ParentMessageId { get; set; }
        public Guid CorrelationId { get; set; }
        public DateTime Timestamp { get; set; }
        public string ApplicationId { get; set; }
        public Guid? SagaId { get; set; }
    }

    private sealed class FakeSagaHandler { }
}