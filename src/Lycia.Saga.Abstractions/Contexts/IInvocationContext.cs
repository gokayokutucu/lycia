using Lycia.Saga.Abstractions.Messaging;

namespace Lycia.Saga.Abstractions.Contexts;

public interface IInvocationContext
{
    IMessage Message { get; set; }
    ISagaContext? SagaContext { get; set; }
    Type HandlerType { get; set; } 
    Guid? SagaId { get; set; }
    string ApplicationId { get; set; }
    CancellationToken CancellationToken { get; set; }
    Exception? LastException { get; set; }
}