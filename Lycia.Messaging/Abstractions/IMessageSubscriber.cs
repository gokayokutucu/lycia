using System;

namespace Lycia.Messaging.Abstractions
{
    /// <summary>
    /// Manages event subscriptions and their lifecycle for message consumption.
    /// </summary>
    public interface IMessageSubscriber : IDisposable
    {
        /// <summary>
        /// Subscribes to a specific event type on a given queue, bound to an exchange with a routing key.
        /// Event handlers for <typeparamref name="TEvent"/> will be resolved from DI.
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to. Must be a class.</typeparam>
        /// <param name="queueName">The name of the queue to consume from.</param>
        /// <param name="exchangeName">The name of the exchange the queue should be bound to.</param>
        /// <param name="routingKey">The routing key for binding the queue to the exchange.</param>
        /// <returns>The current <see cref="IMessageSubscriber"/> instance for fluent configuration.</returns>
        IMessageSubscriber Subscribe<TEvent>(string queueName, string exchangeName, string routingKey)
            where TEvent : class;

        /// <summary>
        /// Starts listening for messages on all configured subscriptions.
        /// This method typically blocks or runs in the background until Dispose is called.
        /// </summary>
        void StartListening();
    }
}
