using System;
using System.Threading.Tasks;
using Lycia.Messaging; // For IEvent, EventBase
using Lycia.Messaging.Abstractions; // For IMessagePublisher
using Lycia.Saga.Abstractions; // For IEventBus
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lycia.Infrastructure.Eventing
{
    public class RabbitMqEventBus : IEventBus
    {
        private readonly IMessagePublisher _messagePublisher;
        private readonly ILogger<RabbitMqEventBus> _logger;
        private const string DefaultExchangeName = "saga_events_exchange"; // Consistent with previous setup

        public RabbitMqEventBus(IMessagePublisher messagePublisher, ILogger<RabbitMqEventBus>? logger)
        {
            _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
            _logger = logger ?? NullLogger<RabbitMqEventBus>.Instance;
        }

        public Task Send<TCommand>(TCommand command, Guid sagaId) where TCommand : ICommand
        {
            // This IEventBus implementation focuses on publishing events.
            // Sending commands directly via this event bus to a specific saga instance (identified by sagaId)
            // would typically involve a different mechanism, like a command dispatcher that knows
            // how to route commands to specific saga handlers or instances, possibly using a dedicated queue per saga type or instance.
            // For now, this aligns with InMemoryEventBus which also routes through ISagaDispatcher.
            // If ISagaDispatcher is RabbitMQ-aware, it would handle it. If not, direct command sending to RabbitMQ via this method is not fully specified.
            // The existing InMemoryEventBus calls _sagaDispatcher.DispatchAsync(command), which is not directly using RabbitMQ.
            // To make this RabbitMQ-aware for commands, we'd need a convention for command queues/routing.
            _logger.LogWarning("RabbitMqEventBus.Send for CommandType {CommandType} with SagaId {SagaId} is not fully implemented for direct RabbitMQ command routing. Relies on IMessagePublisher or specific command setup.", 
                typeof(TCommand).Name, sagaId);
            // A simple approach if commands were to be published like events (not typical for point-to-point commands):
            // string routingKey = typeof(TCommand).Name.ToLowerInvariant();
            // Guid messageId = (command is IMessage msg) ? msg.MessageId : Guid.Empty;
            // _logger.LogDebug("Publishing command {CommandType} (MessageId: {MessageId}, SagaId: {SagaId}) to Exchange: {ExchangeName}, RoutingKey: {RoutingKey} via RabbitMqEventBus.Send", 
            //     typeof(TCommand).Name, messageId, sagaId, DefaultExchangeName, routingKey);
            // return _messagePublisher.PublishAsync(DefaultExchangeName, routingKey, command);
            return Task.CompletedTask; // Or throw NotImplementedException if preferred
        }

        public Task Publish<TEvent>(TEvent anEvent, Guid sagaIdFromParameter) where TEvent : IEvent
        {
            if (anEvent == null)
            {
                throw new ArgumentNullException(nameof(anEvent));
            }

            Guid finalSagaIdToLog = sagaIdFromParameter; // Default to parameter

            if (anEvent is EventBase eventWithSagaId)
            {
                if (eventWithSagaId.SagaId == Guid.Empty && sagaIdFromParameter != Guid.Empty)
                {
                    _logger.LogDebug("SagaId for EventType {EventType} (MessageId: {MessageId}) was empty, setting from parameter: {SagaIdParameter}", 
                        typeof(TEvent).Name, eventWithSagaId.MessageId, sagaIdFromParameter);
                    eventWithSagaId.SagaId = sagaIdFromParameter;
                    finalSagaIdToLog = sagaIdFromParameter;
                }
                else if (eventWithSagaId.SagaId != Guid.Empty)
                {
                    finalSagaIdToLog = eventWithSagaId.SagaId; // Event's SagaId takes precedence if set
                    if (sagaIdFromParameter != Guid.Empty && eventWithSagaId.SagaId != sagaIdFromParameter)
                    {
                        _logger.LogWarning("SagaId in EventType {EventType} (MessageId: {MessageId}), EventSagaId {EventSagaId}, differs from SagaId parameter {ParameterSagaId}. Using SagaId from event.", 
                            typeof(TEvent).Name, eventWithSagaId.MessageId, eventWithSagaId.SagaId, sagaIdFromParameter);
                    }
                }
                else // eventWithSagaId.SagaId is empty AND sagaIdFromParameter is empty
                {
                     _logger.LogWarning("No SagaId provided in EventType {EventType} (MessageId: {MessageId}) or as parameter. Publishing without explicit SagaId association logic here.", 
                        typeof(TEvent).Name, eventWithSagaId.MessageId);
                     finalSagaIdToLog = Guid.Empty; // Reflect that no specific SagaId is being used
                }
            }
            else
            {
                _logger.LogWarning("Event of type {EventType} (MessageId: {MessageId}) does not inherit from EventBase. Cannot verify or set SagaId directly. Using SagaId parameter {SagaIdParameter} for logging.", 
                    typeof(TEvent).Name, (anEvent as IMessage)?.MessageId, sagaIdFromParameter);
            }

            string routingKey = typeof(TEvent).Name.ToLowerInvariant(); 
            
            _logger.LogInformation(
                "Publishing EventType: {EventType}, MessageId: {MessageId}, EffectiveSagaId: {EffectiveSagaId}, to Exchange: {ExchangeName}, RoutingKey: {RoutingKey}",
                typeof(TEvent).Name, (anEvent as IMessage)?.MessageId, finalSagaIdToLog, DefaultExchangeName, routingKey);

            return _messagePublisher.PublishAsync(DefaultExchangeName, routingKey, anEvent);
        }
    }
}
