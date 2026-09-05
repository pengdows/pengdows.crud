using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates DuckDB-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: SQLSTATE (23505/23503/23502/23514/25006) → message patterns
/// (constraint violations, then read-only) → timeout → fallback.
/// SQLSTATE and message patterns are checked first because DuckDB error messages include
/// the violating row values, which may contain user data such as "timeout" and would
/// otherwise trigger a false-positive timeout classification.
/// Message-pattern fallback covers drivers that do not populate SqlState.
/// </remarks>
internal sealed class DuckDbExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var message = exception.Message;

        // SQLSTATE-first: DuckDB uses standard ANSI SQL class-23 codes (same as PostgreSQL)
        if (!string.IsNullOrWhiteSpace(sqlState))
        {
            if (sqlState == "23505")
            {
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (sqlState == "23503")
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (sqlState == "23502")
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (sqlState == "23514")
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            // 25006 = READ_ONLY_SQL_TRANSACTION: write attempted on a read-only connection
            if (sqlState == "25006")
            {
                return DbExceptionTranslationSupport.CreateReadOnlyViolation(database, exception, operationKind);
            }
        }

        // Message-based fallback for cases where the driver does not populate SqlState.
        // DuckDB constraint error messages follow the pattern:
        //   "Constraint Error: Duplicate key 'x' violates unique constraint 'name'"
        //   "Constraint Error: NOT NULL constraint failed: table.column"
        //   "Constraint Error: CHECK constraint failed: constraint_name"
        //   "Constraint Error: Violates foreign key constraint ..."
        if (message.Contains("Duplicate key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("primary key constraint", StringComparison.OrdinalIgnoreCase))
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("foreign key", StringComparison.OrdinalIgnoreCase))
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("NOT NULL constraint", StringComparison.OrdinalIgnoreCase))
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        // DuckDB read-only access mode violation (message-based fallback when SqlState is absent).
        // DuckDB enforces read-only at the connection/binder level and rejects writes before execution:
        //   "Binder Error: Cannot execute statement of type "INSERT" on database "..." which is attached in read-only mode!"
        //   "Binder Error: Cannot execute statement of type "UPDATE" on database "..." which is attached in read-only mode!"
        // The error fires at bind time, not after partial execution, and is non-retryable.
        // Also catches other drivers/wrappers that surface similar messages.
        // Checked before timeout to prevent false-positive timeout classification when
        // the word "timeout" appears in a read-only error message.
        if (message.Contains("read-only", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("read only", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateReadOnlyViolation(database, exception, operationKind);
        }

        // DuckDB has no lock-based deadlock concept; concurrent writers instead race under MVCC
        // and the loser's execute/commit throws "TransactionContext Error: Conflict on ...!" at
        // commit/execute time (confirmed against DuckDB.NET.Data.Full: "Conflict on update!",
        // "Conflict on tuple deletion!"). This is the DuckDB analog of the deadlock/serialization
        // conflicts every other multi-writer-capable dialect here (Postgres/MySQL/SQL
        // Server/Oracle) maps to a transient, retryable exception -- without this branch it fell
        // through to the non-transient fallback, leaving callers with no retry signal for a
        // condition that legitimately succeeds on retry (e.g. a Hangfire job and a web request
        // racing to update the same row in a shared DuckDB file).
        // Checked before timeout for the same false-positive-on-embedded-context-text reason as
        // the other DuckDB categories above.
        if (message.Contains("TransactionContext Error: Conflict on", StringComparison.OrdinalIgnoreCase))
        {
            return new SerializationConflictException(
                $"{operationKind} hit a write-write conflict on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        // DuckDB is embedded and takes an OS-level file lock for the database file; a second
        // process opening the same file for read-write while another still holds it open fails
        // at connection-open time with "IO Error: Could not set lock on file "...": Conflicting
        // lock is held in <process> (PID <n>)" (confirmed empirically against DuckDB.NET.Data.Full
        // 1.4.1). This is exactly the shape hit when e.g. a Hangfire worker process and a web
        // request process both touch the same DuckDB-backed tenant file concurrently -- but
        // unlike the write-write conflict above, it is NOT automatically transient: it only
        // clears if the other process closes its connection, and if that other process is a
        // second writer that was never supposed to exist against this file, retrying never
        // succeeds. See FileLockContentionException for why this gets its own type with
        // IsTransient hardcoded false, rather than being lumped in as a generic retryable
        // ConnectionException.
        if (message.Contains("Could not set lock on file", StringComparison.OrdinalIgnoreCase))
        {
            return new FileLockContentionException(
                $"{operationKind} could not open a DuckDB connection because another process holds the file lock: {message}",
                database, exception, errorCode: errorCode);
        }

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
