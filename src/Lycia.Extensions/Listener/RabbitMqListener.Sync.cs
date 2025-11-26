// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

#if NETSTANDARD2_0
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lycia.Extensions.Listener;

public class RabbitMqListener : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RabbitMqListener> _logger;
    private readonly IMessageSerializer _serializer;
    private Thread _workerThread;
    private CancellationTokenSource _cts;
    private bool _running;

    public RabbitMqListener(IServiceProvider serviceProvider, IEventBus eventBus, ILogger<RabbitMqListener> logger, IMessageSerializer messageSerializer)
    {
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _logger = logger;
        _serializer = messageSerializer;
        _cts = new CancellationTokenSource();

        _workerThread = new Thread(() => Run(_cts.Token));
        _workerThread.IsBackground = true;
        _workerThread.Start();
    }

    protected void Run(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RabbitMqListener started");

        _running = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var sagaDispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();
                    var enumerator = _eventBus.ConsumeWithAck(stoppingToken).GetEnumerator();
                    try
                    {
                        while (true)
                        {
                            var moveNext = enumerator.MoveNext();
                            if (!moveNext)
                                break;

                            var body = enumerator.Current.Body;
                            var messageType = enumerator.Current.MessageType;
                            var handlerType = enumerator.Current.HandlerType;
                            var headers = enumerator.Current.Headers;
                            var ack = enumerator.Current.Ack;
                            var nack = enumerator.Current.Nack;

                            if (stoppingToken.IsCancellationRequested)
                                break;

                            var (_, serCtx) = _serializer.CreateContextFor(messageType);
                            var normalizedHeaders = _serializer.NormalizeTransportHeaders(headers);
                            var deserialized = _serializer.Deserialize(body, normalizedHeaders, serCtx);

                            _logger.LogInformation("Dispatching {MessageType} to SagaDispatcher", messageType.Name);
                            // Find the generic DispatchAsync<TMessage>(TMessage message, Type? handlerType, Guid? sagaId, CancellationToken cancellationToken) method
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
                            ack();
                        }
                    }
                    finally
                    {
                        enumerator.Dispose();
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
#endif
