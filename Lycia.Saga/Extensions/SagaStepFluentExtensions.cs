using Lycia.Messaging;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga.Extensions;

public static class SagaStepFluentExtensions
{
    public static async Task ThenMarkAsComplete<TStep, TInitialMessage>(this Task operation,
        ISagaContext<TInitialMessage> context)
        where TStep : IMessage
        where TInitialMessage : IMessage
    {
        await operation;
        await context.MarkAsComplete<TStep>();
    }

    public static async Task ThenMarkAsFailed<TStep, TInitialMessage>(this Task operation, ISagaContext<TInitialMessage> context)
        where TStep : IMessage
        where TInitialMessage : IMessage
    {
        await operation;
        await context.MarkAsFailed<TStep>();
    }    
    
    public static async Task ThenMarkAsCompensationFailed<TStep, TInitialMessage>(this Task operation, ISagaContext<TInitialMessage> context)
        where TStep : IMessage
        where TInitialMessage : IMessage
    {
        await operation;
        await context.MarkAsCompensationFailed<TStep>();
    }
}