using Lycia.Messaging;
using Lycia.Messaging.Enums;
using Lycia.Saga.Abstractions;

namespace Lycia.Saga;

// TInitialMessage is the type of message that the ISagaContext is primarily associated with.
public class ReactiveSagaStepFluent<TStep, TInitialMessage>(
    ISagaContext<TInitialMessage> context,
    Func<Task> operation,
    TInitialMessage message)
    where TStep : IMessage
    where TInitialMessage : IMessage
{
    private TInitialMessage _message = message;

    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Completed;
        _message.__TestStepType = typeof(TInitialMessage);
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Completed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsComplete<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Failed;
        _message.__TestStepType = typeof(TInitialMessage);
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Failed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsFailed<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Compensated;
        _message.__TestStepType = typeof(TInitialMessage);
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Compensated, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsCompensated<TInitialMessage>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.CompensationFailed;
        _message.__TestStepType = typeof(TInitialMessage);
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.CompensationFailed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsCompensationFailed<TInitialMessage>();
#endif
    }
}

public class CoordinatedSagaStepFluent<TStep, TSagaData>(
    ISagaContext<TStep, TSagaData> context,
    Func<Task> operation,
    TStep @event)
    where TStep : IMessage
    where TSagaData : SagaData
{
    private TStep _message = @event;

    public async Task ThenMarkAsComplete()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Completed;
        _message.__TestStepType = _message.GetType();
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Completed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsComplete<TStep>();
#endif
    }

    public async Task ThenMarkAsFailed(FailResponse fail)
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Failed;
        _message.__TestStepType = _message.GetType();
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Failed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsFailed<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensated()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.Compensated;
        _message.__TestStepType = _message.GetType();
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.Compensated, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsCompensated<TStep>();
#endif
    }

    public async Task ThenMarkAsCompensationFailed()
    {
#if UNIT_TESTING
        _message.__TestStepStatus = StepStatus.CompensationFailed;
        _message.__TestStepType = _message.GetType();
        await context.SagaStore.LogStepAsync(_message.SagaId!.Value, _message.MessageId, _message.ParentMessageId,
            _message.__TestStepType,
            StepStatus.CompensationFailed, context.HandlerType, _message);
        await operation();
#else
        await operation();
        await context.MarkAsCompensationFailed<TStep>();
#endif
    }
}