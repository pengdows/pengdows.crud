namespace pengdows.crud.exceptions;

public class TooManyColumns
    : Exception
{
    public TooManyColumns(string message) : base(message)
    {
    }
}