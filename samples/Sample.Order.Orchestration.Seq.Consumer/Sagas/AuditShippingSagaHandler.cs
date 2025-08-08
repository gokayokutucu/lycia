using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Events;
using Sample.Shared.SagaStates;

namespace Sample.Order.Orchestration.Seq.Consumer.Sagas;

public class AuditShippingSagaHandler : 
    CoordinatedSagaHandler<PaymentProcessedEvent, CreateOrderSagaData>
{
    public override async Task HandleAsync(PaymentProcessedEvent message)
    {
        try
        {
            await Context.MarkAsFailed<PaymentProcessedEvent>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"🚨 Audit failed: {e.Message}");
            await Context.MarkAsFailed<PaymentProcessedEvent>();
        }
    }

    public override async Task CompensateAsync(PaymentProcessedEvent message)
    {
        try
        {
            await Context.CompensateAndBubbleUp<PaymentProcessedEvent>();
        }
        catch (Exception e)
        {
            await Context.MarkAsCompensationFailed<PaymentProcessedEvent>();
            throw;
        }
    }
}