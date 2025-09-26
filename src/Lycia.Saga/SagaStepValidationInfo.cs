// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Saga.Enums;

namespace Lycia.Saga;

public class SagaStepValidationInfo(SagaStepValidationResult validationResult, string? message= null)
{
    public SagaStepValidationResult ValidationResult { get; } = validationResult;
    public string Message { get; } = message ?? string.Empty;
}