namespace Lycia.Saga.Exceptions;

public class SagaDispatchException(string message, System.Exception? inner = null)
    : System.Exception(message, inner) { }
