using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates DuckDB-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: connection (message-based) → file-lock contention → constraint-kind
/// (delegated to the dialect) → ReadOnlyViolation/SerializationFailure/Timeout (delegated to the
/// dialect's ClassifyException) → fallback. Checked before the delegated category classification
/// because DuckDB error messages include the violating row values, which may contain user data
/// such as "timeout" and would otherwise trigger a false-positive timeout classification.
/// DuckDB is an embedded, file-based engine with no TCP connection concept — its closest
/// analog to a "connection failure" is a file-open failure (bad path, permissions, corrupt
/// file), matching how SqliteExceptionTranslator treats SQLITE_CANTOPEN/SQLITE_NOTADB.
/// Confirmed against a real DuckDBException: opening a nonexistent path reports
/// DuckDBException.ErrorType == Invalid (NOT a more specific "Io"/"Connection" value — the
/// driver's ErrorType enum is not a reliable signal for this specific failure mode), with
/// message "DuckDBOpen failed: IO Error: Cannot open file "..." ...". Message text is the
/// only reliable trigger here.
/// </remarks>
internal sealed class DuckDbExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var message = exception.Message;

        if (message.Contains("Cannot open file", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Distinct from the missing/inaccessible-path case above: DuckDB is embedded and takes an
        // OS-level file lock, so a second process opening the same (valid, existing) file for
        // read-write while a first still holds it open fails at connection-open time with
        // "IO Error: Could not set lock on file "...": Conflicting lock is held in <process>
        // (PID <n>)" (confirmed empirically against DuckDB.NET.Data.Full 1.4.1 by racing two
        // processes for the same file). Exactly the shape hit when a Hangfire worker process and
        // a web request process both touch the same DuckDB-backed tenant file -- but unlike the
        // missing-path case or the write-write conflict below, it is NOT automatically transient:
        // it only clears if the other process closes its connection, and if that other process is
        // a second writer that was never supposed to exist against this file, retrying never
        // succeeds. See FileLockContentionException for why this gets its own type with
        // IsTransient hardcoded false, rather than being lumped in as a generic retryable
        // ConnectionException.
        if (message.Contains("Could not set lock on file", StringComparison.OrdinalIgnoreCase))
        {
            return new FileLockContentionException(
                $"{operationKind} could not open a DuckDB connection because another process holds the file lock: {message}",
                database, exception, errorCode: errorCode);
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is delegated to the dialect —
        // see IDbExceptionTranslator.Translate's doc comment. DuckDbDialect's overrides check the
        // identical SQLSTATE-first-then-message-pattern signals this translator used to check
        // directly, so this is a behavior-preserving delegation, not a narrowing.
        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }
        }

        // ReadOnlyViolation (both SqlState 25006 and the message-based "Binder Error: ... attached
        // in read-only mode!" fallback), SerializationFailure ("Conflict on" MVCC conflicts), and
        // Timeout classification are delegated to the dialect's single ClassifyException/
        // TryClassifyProviderException source — see DbExceptionTranslationSupport.
        // TryCreateFromCategory's doc comment. DuckDbDialect's override checks the identical
        // signals this translator used to check directly, so this is a behavior-preserving
        // delegation, not a narrowing.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
