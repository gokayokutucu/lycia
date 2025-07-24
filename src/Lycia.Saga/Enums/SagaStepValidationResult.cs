namespace Lycia.Saga.Enums;

public enum SagaStepValidationResult
{
    ValidTransition,
    Idempotent,                 // Same status, same payload (legal)
    DuplicateWithDifferentPayload, // Same status, different payload (illegal)
    InvalidTransition,          // Not intended transition (illegal)
    CircularChain               // Interconnected parent/child chain (illegal, e.g. A -> B -> C -> A)
}