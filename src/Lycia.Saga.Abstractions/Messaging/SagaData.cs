// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Abstractions.Messaging;

public abstract class SagaData
{
    public Guid SagaId { get; set; }
    public bool IsCompleted { get; set; }
    public Type? FailedStepType { get; set; }
    public Type? FailedHandlerType { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
}