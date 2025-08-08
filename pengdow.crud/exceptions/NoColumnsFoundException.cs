namespace pengdow.crud.exceptions;

public class NoColumnsFoundException : Exception
{
    public NoColumnsFoundException(string message) : base(message)
    {
    }
}