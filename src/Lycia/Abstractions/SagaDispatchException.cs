namespace Lycia.Abstractions;

public class SagaDispatchException(string message, System.Exception? inner = null)
    : System.Exception(message, inner) { }
