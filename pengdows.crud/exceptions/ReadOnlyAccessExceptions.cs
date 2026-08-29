namespace pengdows.crud.exceptions;

/// <summary>Read-only context configuration rejected a write operation.</summary>
public sealed class ReadOnlyContextException : NotSupportedException, IReadOnlyViolation
{
    public ReadOnlyContextException(string message) : base(message)
    {
    }
}

/// <summary>Read-only connection or transaction intent rejected a write operation.</summary>
public sealed class ReadOnlyAccessException : InvalidOperationException, IReadOnlyViolation
{
    public ReadOnlyAccessException(string message) : base(message)
    {
    }
}
