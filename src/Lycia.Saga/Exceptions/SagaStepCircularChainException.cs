// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Saga.Exceptions;

public class SagaStepCircularChainException(string message) : InvalidOperationException(message);