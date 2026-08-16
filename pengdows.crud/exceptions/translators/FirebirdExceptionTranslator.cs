using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Firebird-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: connection (SQLSTATE class 08) → unique/PK constraint → FK → NOT NULL →
/// CHECK → timeout → fallback.
/// Message-based detection is used for constraint violations because Firebird wraps ISC codes
/// inside FbException.Errors; extracting them via reflection is fragile across provider
/// versions. The violation message text is stable across Firebird 3–5. Connection failures are
/// SQLSTATE-based instead: FbException exposes a real (all-caps) "SQLSTATE" property — confirmed
/// against a live container, a closed-port connect attempt reports SQLSTATE "08006" (ANSI
/// connection-exception class) with message "Unable to complete network request to host ...".
/// </remarks>
internal sealed class FirebirdExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);

        // Checked first: a connection-refused/timed-out message could otherwise be swallowed by
        // either the constraint checks below (unlikely) or the LooksLikeTimeout heuristic (a
        // "connection timed out" OS error would literally contain the word "timeout").
        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Check constraint violations BEFORE LooksLikeTimeout: Firebird embeds the failed
        // key value in the exception message, and key values may contain "timeout" (e.g.
        // distributed lock resource names like "lock-timeout-{guid}"), which would otherwise
        // cause the timeout heuristic to fire and swallow a legitimate PK violation.
        if (message.Contains("violation of PRIMARY", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("violation of UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("*** null ***", StringComparison.OrdinalIgnoreCase))
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

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
