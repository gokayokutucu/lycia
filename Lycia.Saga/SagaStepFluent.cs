using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(ISagaContext<TInitialMessage> context, Task operation, TStep @event)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Completed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsComplete<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Failed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsFailed<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Compensated;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsCompensated<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.CompensationFailed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsCompensationFailed<TInitialMessage>();
#endif
    }
}

public class CoordinatedSagaStepFluent<TStep, TSagaData>(ISagaContext<TStep, TSagaData> context, Task operation, TStep @event)
    where TStep : IMessage
    where TSagaData : SagaData
{
    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Completed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsComplete<TStep>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Failed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsFailed<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.Compensated;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsCompensated<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
            @event.__TestStepStatus = StepStatus.CompensationFailed;
            @event.__TestStepType = @event.GetType();
            await operation;
#else
        await operation;
        await context.MarkAsCompensationFailed<TStep>();
#endif
    }
}