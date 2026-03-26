using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

public class DatabaseOperationException : DatabaseException
{
    public DatabaseOperationException(
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

/// <summary>
/// Thrown when an optimistic-concurrency conflict is detected (e.g. version mismatch on UPDATE).
/// </summary>
/// <remarks>
/// Inherits <see cref="DatabaseOperationException"/> → <see cref="DatabaseException"/>, so a
/// <c>catch (DatabaseException)</c> block will catch it. <see cref="DatabaseException.IsTransient"/>
/// is <see langword="null"/> by default — concurrent conflicts are not automatically retryable.
/// </remarks>
public class ConcurrencyConflictException : DatabaseOperationException
{
    public ConcurrencyConflictException(
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

public class CommandTimeoutException : DatabaseOperationException
{
    public CommandTimeoutException(
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

/// <summary>
/// Thrown when the driver cannot open or maintain a connection to the database server.
/// </summary>
/// <remarks>
/// Includes the <see cref="DatabaseException.SqlState"/> (SQLSTATE class 08) and/or
/// <see cref="DatabaseException.ErrorCode"/> reported by the provider. Callers that need
/// to distinguish connection failures from other database errors should catch this type
/// rather than the base <see cref="DatabaseException"/>.
/// </remarks>
public class ConnectionException : DatabaseOperationException
{
    public ConnectionException(
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

/// <summary>
/// Thrown when a write operation is attempted on a connection opened in read-only mode.
/// </summary>
/// <remarks>
/// Occurs for SQLite (SQLITE_READONLY, error code 8) and DuckDB (SQLSTATE 25006 or
/// <c>access_mode=READ_ONLY</c> write attempts) when a modifying statement reaches a
/// read-only connection. Not transient — the caller must use a writable context.
/// </remarks>
public class ReadOnlyViolationException : DatabaseOperationException
{
    public ReadOnlyViolationException(
        string message,
        SupportedDatabase database,
        Exception? innerException = null,
        string? sqlState = null,
        int? errorCode = null)
        : base(message, database, innerException, sqlState, errorCode, constraintName: null, isTransient: false)
    {
    }
}

/// <summary>
/// Thrown when a transaction operation (begin, commit, or rollback) fails at the driver level.
/// </summary>
/// <remarks>
/// After a <c>TransactionException</c> is thrown from <c>Commit</c> or <c>Rollback</c>,
/// <c>TransactionContext.IsCompleted</c> is set to <see langword="true"/> and the underlying
/// connection has already been released. The <c>Dispose</c> of the owning
/// <c>TransactionContext</c> will not attempt a second rollback.
/// </remarks>
public class TransactionException : DatabaseOperationException
{
    public TransactionException(
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
