using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

public class TransientWriteConflictException : DatabaseOperationException
{
    public TransientWriteConflictException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = true)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class DeadlockException : TransientWriteConflictException
{
    public DeadlockException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = true)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}

public sealed class SerializationConflictException : TransientWriteConflictException
{
    public SerializationConflictException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null,
        string? constraintName = null,
        bool? isTransient = true)
        : base(message, database, innerException, sqlState, errorCode, constraintName, isTransient)
    {
    }
}
