namespace Lycia.Exceptions;

public class TransientSagaException : LyciaSagaException
{
    public TransientSagaException(string message) : base(message)
    {
    }

    public TransientSagaException(string message, Exception innerException) : base(message, innerException)
    {
    }
}