using Lycia.Saga.Enums;

namespace Lycia.Saga;

public class SagaStepValidationInfo(SagaStepValidationResult validationResult, string? message= null)
{
    public SagaStepValidationResult ValidationResult { get; } = validationResult;
    public string Message { get; } = message ?? string.Empty;
}