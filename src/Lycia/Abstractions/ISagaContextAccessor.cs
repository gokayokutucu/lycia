namespace Lycia.Abstractions;

public interface ISagaContextAccessor
{
    ISagaContext? Current { get; set; }
}
