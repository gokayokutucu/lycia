using System.Threading;
using System.Threading.Tasks;

namespace OrderService.Application.Contracts.Infrastructure
{
    public interface IMessageBroker
    {
        /// <summary>
        /// Publishes a message to the message broker.
        /// </summary>
        /// <typeparam name="T">The type of the message being published.</typeparam>
        /// <param name="message">The message payload.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A task that represents the asynchronous publish operation.</returns>
        /// <remarks>
        /// The implementation will handle serialization and determining the
        /// appropriate topic, exchange, or queue based on message type or configuration.
        /// </remarks>
        Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    }
}
