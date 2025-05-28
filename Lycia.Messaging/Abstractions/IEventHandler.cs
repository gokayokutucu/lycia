using System.Threading.Tasks;

namespace Lycia.Messaging.Abstractions
{
    /// <summary>
    /// Defines a handler for a specific type of event.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle. Must be a class.</typeparam>
    public interface IEventHandler<TEvent> where TEvent : class
    {
        /// <summary>
        /// Handles the received event asynchronously.
        /// </summary>
        /// <param name="anEvent">The event object to handle.</param>
        /// <returns>A task that represents the asynchronous handling operation.</returns>
        Task HandleAsync(TEvent anEvent);
    }
}
