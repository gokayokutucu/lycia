// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0


using Lycia.Observability;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Lycia.Extensions.Listener;

public class RabbitMqListener(
    IServiceProvider serviceProvider,
    IEventBus eventBus,
    ILogger<RabbitMqListener> logger,
    IMessageSerializer serializer,
    LyciaActivitySourceHolder activitySourceHolder)
#if NET8_0_OR_GREATER
: BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
#elif NETSTANDARD2_0
: IDisposable
{
    private Thread? workerThread;
    private CancellationTokenSource? cts;

    public void Start() // ← call withAutoActivate
    {
        cts = new CancellationTokenSource();
        workerThread = new Thread(async () =>
        {
            await Task.Delay(2000);
            await ExecuteAsync(cts.Token);
        })
        {
            IsBackground = true
        };
        workerThread.Start();
    }

    protected async Task ExecuteAsync(CancellationToken stoppingToken)
#endif
    {
        logger.LogInformation("RabbitMqListener started");

        using var scope = serviceProvider.CreateScope();

        var sagaDispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();

        await foreach (var msg in eventBus.ConsumeWithAckAsync(stoppingToken))
        {
            var (body, messageType, handlerType, headers, ack, nack) = msg;
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var (_, serCtx) = serializer.CreateContextFor(messageType);
                var normalizedHeaders = serializer.NormalizeTransportHeaders(headers);
                var deserialized = serializer.Deserialize(body, normalizedHeaders, serCtx);

                logger.LogInformation("Dispatching {MessageType} to SagaDispatcher", messageType.Name);

                // Build handler name for Activity span
                var handlerName = handlerType?.FullName ?? $"Saga.{messageType.Name}Handler";

                // Extract parent context from headers and start consumer Activity
                var rawHeaders = headers as IDictionary<string, object?>
                                 ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var parentContext = LyciaTracePropagation.Extract(rawHeaders);

                using var activity = parentContext != default
                    ? activitySourceHolder.Source.StartActivity(
                        handlerName,
                        ActivityKind.Consumer,
                        parentContext)
                    : activitySourceHolder.Source.StartActivity(
                        handlerName,
                        ActivityKind.Consumer);

                if (activity != null)
                {
                    // Basic messaging tags
                    activity.SetTag("messaging.system", "rabbitmq");
                    activity.SetTag("messaging.destination", handlerType?.Name ?? "unknown");
                    activity.SetTag("messaging.operation", "process");
                }
                // Find the generic DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken) method
                var dispatchMethod = typeof(ISagaDispatcher)
                    .GetMethods()
                    .FirstOrDefault(m =>
                        m is { Name: nameof(ISagaDispatcher.DispatchAsync), IsGenericMethodDefinition: true }
                        && m.GetParameters().Length == 4);

                if (dispatchMethod == null)
                {
                    logger.LogWarning("No suitable DispatchAsync<TMessage> found for message type {MessageType}", messageType.Name);
                    continue;
                }

                var sagaIdProp = deserialized.GetType().GetProperty("SagaId");
                Guid? sagaId = null;
                if (sagaIdProp != null && sagaIdProp.GetValue(deserialized) is Guid id && id != Guid.Empty)
                    sagaId = id;

                // Make the method generic for the runtime type
                var constructed = dispatchMethod.MakeGenericMethod(deserialized.GetType());

                // Call with all parameters; null for handlerType/sagaId, stoppingToken
                if (constructed.Invoke(sagaDispatcher, [deserialized, handlerType, sagaId, stoppingToken]) is not Task dispatchTask)
                {
                    logger.LogError(
                        "DispatchAsync invocation for message type {MessageType} did not return a Task instance",
                        messageType.Name);
                    continue;
                }
                await dispatchTask;
                await ack();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while processing message of type {MessageType}", messageType.Name);
                try
                {
                    await nack(false);
                }
                catch (Exception nackEx) { logger.LogWarning(nackEx, "Nack failed for {MessageType}", messageType.Name); }
                // Optional: DLQ/retry/metrics logic.
            }
        }

        logger.LogInformation("RabbitMqListener stopped");
    }
#if NETSTANDARD2_0
    public void Dispose()
    {
        cts?.Cancel();
        workerThread?.Join(5000);
        cts?.Dispose();
    }
#endif
}