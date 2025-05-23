namespace Lycia.Messaging;

public interface ISagaCompensationHandler<in TMessage> where TMessage : IMessage
{
    Task CompensateAsync(TMessage message);
}