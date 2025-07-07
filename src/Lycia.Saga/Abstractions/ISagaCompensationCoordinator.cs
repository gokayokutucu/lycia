using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaCompensationCoordinator
{
    Task CompensateAsync(Guid sagaId, Type failedStepType);
    Task CompensateParentAsync(Guid sagaId, Type stepType, IMessage message);
}