using System.Text;
using Lycia.Infrastructure.Abstractions;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lycia.Infrastructure.Listener;

public class RabbitMqListener(
    IServiceProvider serviceProvider,
    IEventBus eventBus,
    ILogger<RabbitMqListener> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RabbitMqListener started");

        using var scope = serviceProvider.CreateScope();

        var sagaDispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();
        
        await foreach (var (body, messageType) in eventBus.ConsumeAsync(stoppingToken))
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var json = Encoding.UTF8.GetString(body);
                var deserialized = JsonConvert.DeserializeObject(json, messageType);

                if (deserialized == null)
                {
                    logger.LogWarning("Failed to deserialize message to type {MessageType}", messageType.Name);
                    continue;
                }

                logger.LogInformation("Dispatching {MessageType} to SagaDispatcher", messageType.Name);
                var dispatchMethod = typeof(ISagaDispatcher)
                    .GetMethod(nameof(ISagaDispatcher.DispatchAsync), [deserialized.GetType()]);

                if (dispatchMethod == null)
                {
                    logger.LogWarning("No suitable DispatchAsync found for message type {MessageType}",
                        messageType.Name);
                    continue;
                }

                if (dispatchMethod.Invoke(sagaDispatcher, [deserialized]) is not Task dispatchTask)
                {
                    logger.LogError(
                        "DispatchAsync invocation for message type {MessageType} did not return a Task instance",
                        messageType.Name);
                    continue;
                }

                await dispatchTask;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while processing message of type {MessageType}", messageType?.Name);
                // Optional: DLQ/retry/metrics logic.
            }
        }

        logger.LogInformation("RabbitMqListener stopped");
    }
}