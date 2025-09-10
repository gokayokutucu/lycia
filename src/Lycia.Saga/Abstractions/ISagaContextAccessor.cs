namespace Lycia.Saga.Abstractions;

public interface ISagaContextAccessor
{
    ISagaContext? Current { get; set; }
}
