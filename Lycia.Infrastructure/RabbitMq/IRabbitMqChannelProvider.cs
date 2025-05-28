using RabbitMQ.Client;

namespace Lycia.Infrastructure.RabbitMq
{
    public interface IRabbitMqChannelProvider : IDisposable
    {
        IModel GetChannel();
    }
}
