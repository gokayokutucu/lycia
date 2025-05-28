using System.Threading.Tasks;

namespace Lycia.Messaging.Abstractions
{
    /// <summary>
    /// Defines a contract for publishing messages to a message broker.
    /// </summary>
    public interface IMessagePublisher
    {
        /// <summary>
        /// Publishes a message asynchronously to the specified exchange with the given routing key.
        /// </summary>
        /// <typeparam name="T">The type of the message to publish. Must be a class.</typeparam>
        /// <param name="exchangeName">The name of the exchange to publish to.</param>
        /// <param name="routingKey">The routing key for the message.</param>
        /// <param name="message">The message object to publish.</param>
        /// <returns>A task that represents the asynchronous publish operation.</returns>
        Task PublishAsync<T>(string exchangeName, string routingKey, T message) where T : class;
    }
}
