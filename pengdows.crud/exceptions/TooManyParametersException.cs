namespace pengdows.crud.exceptions;

public class TooManyParametersException : Exception
{
    public TooManyParametersException(string? message, int contextMaxParameterLimit) : base(message)
    {
        MaxAllowed = contextMaxParameterLimit;
    }

    public int MaxAllowed { get; private set; }
}