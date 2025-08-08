namespace pengdow.crud.exceptions;

public class ConnectionFailedException : Exception

{
    public ConnectionFailedException(string message) : base(message)
    {
    }
}