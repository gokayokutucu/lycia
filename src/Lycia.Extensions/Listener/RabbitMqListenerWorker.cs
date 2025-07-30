using Lycia.Saga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Threading;

namespace Lycia.Extensions.Listener
{
    public class RabbitMqListenerWorker : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEventBus _eventBus;
        private readonly ILogger<RabbitMqListenerWorker> _logger;
        private Thread _workerThread;
        private CancellationTokenSource _cts;
        private bool _running;

        public RabbitMqListenerWorker(IServiceProvider serviceProvider, IEventBus eventBus, ILogger<RabbitMqListenerWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _eventBus = eventBus;
            _logger = logger;
            _cts = new CancellationTokenSource();

            _workerThread = new Thread(() => Run(_cts.Token));
            _workerThread.IsBackground = true;
            _workerThread.Start();
        }

        private void Run(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RabbitMqListener started");

            _running = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var sagaDispatcher = (ISagaDispatcher)scope.ServiceProvider.GetService(typeof(ISagaDispatcher));
                        var enumerator = _eventBus.ConsumeAsync(cancellationToken: stoppingToken).GetAsyncEnumerator();

                        try
                        {
                            while (true)
                            {
                                var moveNext = enumerator.MoveNextAsync().AsTask();
                                moveNext.Wait(stoppingToken);
                                if (!moveNext.Result) break;

                                var (body, messageType, handlerType) = enumerator.Current;
                                var json = Encoding.UTF8.GetString(body);
                                var deserialized = JsonConvert.DeserializeObject(json, messageType);

                                if (deserialized == null)
                                {
                                    _logger.LogWarning("Failed to deserialize message to type {MessageType}", messageType.Name);
                                    continue;
                                }

                                _logger.LogInformation("Dispatching {MessageType} to SagaDispatcher", messageType.Name);
                                var dispatchMethod = typeof(ISagaDispatcher)
                                    .GetMethods()
                                    .FirstOrDefault(m =>
                                        m is { Name: nameof(ISagaDispatcher.DispatchAsync), IsGenericMethodDefinition: true }
                                        && m.GetParameters().Length == 4);

                                if (dispatchMethod == null)
                                {
                                    _logger.LogWarning("No suitable DispatchAsync<TMessage> found for message type {MessageType}", messageType.Name);
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
                                    _logger.LogError(
                                        "DispatchAsync invocation for message type {MessageType} did not return a Task instance",
                                        messageType.Name);
                                    continue;
                                }

                                dispatchTask.GetAwaiter().GetResult();
                            }
                        }
                        finally
                        {
                            enumerator.DisposeAsync().AsTask().Wait(stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing message.");
                }
                Thread.Sleep(1000);
            }
            _running = false;
            _logger.LogInformation("RabbitMqListener stopped");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _workerThread?.Join(5000);
            _cts.Dispose();
        }
    }
}
