using System.Text;
using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lycia.Extensions.Listener;

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
        
        await foreach (var (body, messageType) in eventBus.ConsumeAsync(autoAck: true, cancellationToken:stoppingToken))
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
                // Find the generic DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken) method
                var dispatchMethod = typeof(ISagaDispatcher)
                    .GetMethods()
                    .FirstOrDefault(m =>
                        m is { Name: nameof(ISagaDispatcher.DispatchAsync), IsGenericMethodDefinition: true }
                        && m.GetParameters().Length == 3);

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
                if (constructed.Invoke(sagaDispatcher, [deserialized, sagaId, stoppingToken]) is not Task dispatchTask)
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