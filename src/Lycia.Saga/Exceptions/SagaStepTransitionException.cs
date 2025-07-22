namespace Lycia.Saga.Exceptions;

public class SagaStepTransitionException(string message) : InvalidOperationException(message);