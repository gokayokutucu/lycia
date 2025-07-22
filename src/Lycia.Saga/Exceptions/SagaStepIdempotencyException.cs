namespace Lycia.Saga.Exceptions;

public class SagaStepIdempotencyException(string message) : InvalidOperationException(message);