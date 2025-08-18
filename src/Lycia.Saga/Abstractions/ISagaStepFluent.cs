using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaStepFluent
{
    Task ThenMarkAsComplete();
    Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensated(CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensationFailed();
}