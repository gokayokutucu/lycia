// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Exceptions;

public class SagaStepTransitionException(string message) : InvalidOperationException(message);