// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
#if NET8_0_OR_GREATER
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lycia.Extensions.Listener;

public class RabbitMqListener(
    IServiceProvider serviceProvider,
    IEventBus eventBus,
    ILogger<RabbitMqListener> logger,
    IMessageSerializer serializer)
    : BackgroundService
{
    private readonly IMessageSerializer _serializer = serializer;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                var (_, serCtx) = _serializer.CreateContextFor(messageType);
                var normalizedHeaders = _serializer.NormalizeTransportHeaders(headers);
                var deserialized = _serializer.Deserialize(body, normalizedHeaders, serCtx);

                logger.LogInformation("Dispatching {MessageType} to SagaDispatcher", messageType.Name);
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
} 
#endif