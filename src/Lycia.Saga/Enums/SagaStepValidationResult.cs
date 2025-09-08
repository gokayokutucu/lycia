// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Enums;

public enum SagaStepValidationResult
{
    ValidTransition,
    Idempotent,                 // Same status, same payload (legal)
    DuplicateWithDifferentPayload, // Same status, different payload (illegal)
    InvalidTransition,          // Not intended transition (illegal)
    CircularChain               // Interconnected parent/child chain (illegal, e.g. A -> B -> C -> A)
}