using Lycia.Saga.Handlers;
using Sample_Net48.Shared.Messages.Events;
using System;
using System.Threading.Tasks;

namespace Sample_Net48.Shared.Messages.Sagas
{
    public class AuditOrderSagaHandler : ReactiveSagaHandler<OrderCreatedEvent>
    {
        public override Task HandleAsync(OrderCreatedEvent message)
        {
            try
            {
                return Context.MarkAsComplete<OrderCreatedEvent>();
            }
            catch (Exception e)
            {
                Console.WriteLine($"🚨 Audit failed: {e.Message}");
                return Context.MarkAsFailed<OrderCreatedEvent>();
            }
        }
    }
}