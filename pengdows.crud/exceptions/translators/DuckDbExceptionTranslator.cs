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

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
