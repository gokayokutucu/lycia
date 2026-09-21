using Lycia.Saga.Messaging.Handlers; using Lycia.Samples.Microservices.Contracts;
namespace Lycia.Samples.Microservices.Payment;
public sealed class PaymentHandler:CoordinatedResponsiveSagaHandler<ProcessPaymentCommand,PaymentSucceededResponse,ServiceSagaData>
{
    public override async Task HandleAsync(ProcessPaymentCommand message,CancellationToken token=default)
    {
        if (message.InjectFailure)
            throw new InvalidOperationException("Injected payment failure.");
        Context.Data.OrderId=message.OrderId;
        await Context.RespondWithTracking(message,new PaymentSucceededResponse{OrderId=message.OrderId},token)
            .ThenMarkAsComplete<ProcessPaymentCommand>(token);
    }
}
