namespace Lycia.Saga.Exceptions;

public class SagaStepCircularChainException(string message) : InvalidOperationException(message);