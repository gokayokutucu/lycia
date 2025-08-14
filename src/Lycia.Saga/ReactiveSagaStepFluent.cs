using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

public class ReactiveSagaStepFluent<TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<Task> operation) : ISagaStepFluent
    where TInitialMessage : IMessage
{
    public static object Create(Type stepType, object context, Func<Task> operation)
    {
        var open = typeof(ReactiveSagaStepFluent<>);
        var closed = open.MakeGenericType(stepType);
        return Activator.CreateInstance(closed, context, operation)!;
    }
    
    public async Task ThenMarkAsComplete(CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsComplete<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsFailed<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsCompensated(CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsCompensated<TInitialMessage>(cancellationToken);
    }

    public async Task ThenMarkAsCompensationFailed(CancellationToken cancellationToken = default)
    {
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>(cancellationToken);
    }
}