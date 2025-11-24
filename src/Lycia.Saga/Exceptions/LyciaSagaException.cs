namespace Lycia.Saga.Exceptions;

public abstract class LyciaSagaException : Exception
{
    protected LyciaSagaException(string message) : base(message) { }
    protected LyciaSagaException(string message, Exception innerException) : base(message, innerException) { }
}