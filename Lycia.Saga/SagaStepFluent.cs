using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(ISagaContext<TInitialMessage> context, Func<Task> operation, TStep @event)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    private TStep _event = @event;

    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Completed;
            _event.__TestStepType = typeof(TInitialMessage);
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Completed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsComplete<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Failed;
            _event.__TestStepType = typeof(TInitialMessage);
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Failed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsFailed<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Compensated;
            _event.__TestStepType = typeof(TInitialMessage);
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Compensated, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsCompensated<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.CompensationFailed;
            _event.__TestStepType = typeof(TInitialMessage);
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.CompensationFailed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>();
#endif
    }
}

public class CoordinatedSagaStepFluent<TStep, TSagaData>(ISagaContext<TStep, TSagaData> context, Func<Task> operation, TStep @event)
    where TStep : IMessage
    where TSagaData : SagaData
{
    private TStep _event = @event;

    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Completed;
            _event.__TestStepType = _event.GetType();
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Completed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsComplete<TStep>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Failed;
            _event.__TestStepType = _event.GetType();
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Failed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsFailed<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.Compensated;
            _event.__TestStepType = _event.GetType();
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.Compensated, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsCompensated<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
            _event.__TestStepStatus = StepStatus.CompensationFailed;
            _event.__TestStepType = _event.GetType();
            await context.SagaStore.LogStepAsync(_event.SagaId!.Value, _event.__TestStepType, StepStatus.CompensationFailed, context.HandlerType);
            await operation();
#else
        await operation();
        await context.MarkAsCompensationFailed<TStep>();
#endif
    }
}