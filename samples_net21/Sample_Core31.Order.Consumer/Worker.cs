using Lycia.Saga.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample_Core31.Order.Consumer
{
    public class Worker : BackgroundService
    {
        private readonly IEventBus _eventBus;

        public Worker(IEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Example: receive and process events in background
            await foreach (var (body, messageType) in _eventBus.ConsumeAsync(cancellationToken: stoppingToken))
            {
                // TODO: Handle the message
                Console.WriteLine($"Received: {messageType}");
            }
        }
    }
}
