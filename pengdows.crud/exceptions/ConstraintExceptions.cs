using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

public abstract class ConstraintViolationException : DatabaseOperationException
{
    protected ConstraintViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class UniqueConstraintViolationException : ConstraintViolationException
{
    public UniqueConstraintViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class ForeignKeyViolationException : ConstraintViolationException
{
    public ForeignKeyViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class NotNullViolationException : ConstraintViolationException
{
    public NotNullViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class CheckConstraintViolationException : ConstraintViolationException
{
    public CheckConstraintViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}
