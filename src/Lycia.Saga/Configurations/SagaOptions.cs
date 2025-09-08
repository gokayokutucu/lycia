namespace Lycia.Saga.Configurations;

public class SagaOptions
{
    public bool? DefaultIdempotency { get; set; } = true;
    
    public static readonly string Saga = "Lycia:Saga";
}