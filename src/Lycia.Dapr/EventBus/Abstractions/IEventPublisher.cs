namespace Lycia.Dapr.EventBus.Abstractions;

    public interface IEventPublisher
    {
        Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken);
    }