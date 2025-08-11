using Lycia.Messaging;

namespace Lycia.Saga.Abstractions;

public interface ISagaStepFluent
{
    Task ThenMarkAsComplete();
    Task ThenMarkAsFailed(FailResponse fail);
    Task ThenMarkAsCompensated();
    Task ThenMarkAsCompensationFailed();
}