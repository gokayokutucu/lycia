// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Configurations;

public class SagaOptions
{
    public bool? DefaultIdempotency { get; set; } = true;
    
    public static readonly string Saga = "Lycia:Saga";
}