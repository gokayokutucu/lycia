using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaCompensationCoordinator
{
    Task CompensateAsync(Guid sagaId, Type failedStepType, Type? handlerType, IMessage message);
    Task CompensateParentAsync(Guid sagaId, Type stepType, Type handlerType, IMessage message);
}