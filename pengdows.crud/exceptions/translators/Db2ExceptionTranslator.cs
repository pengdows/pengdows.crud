using System.Text.RegularExpressions;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Db2 (Db2 for Linux/Unix/Windows) specific exceptions into the pengdows.crud
/// exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: SQLSTATE (ANSI-standard, primary) → SQLCODE magnitude (fallback,
/// sign-tolerant since it is not yet confirmed whether IBM.Data.Db2's DB2Exception reports
/// SQLCODE as its native negative value or an unsigned magnitude — verify against a live
/// driver exception in Phase 2) → timeout → fallback.
/// Db2 SQLSTATE/SQLCODE pairs used:
///   23505 / -803  unique constraint (index) violation
///   23503 / -530, -531, -532  foreign key (referential integrity) violation on insert/update
///   23504 / -532  foreign key (referential integrity) violation on delete (RESTRICT)
///   23502 / -407  not-null violation
///   23513 / -545  check constraint violation
///   40001 / -911, -913  deadlock or lock timeout (SQLSTATE 40001 cannot by itself
///     distinguish the two on Db2 — treated as SerializationConflictException here,
///     matching the classification used in SqlDialect.TryClassifyProviderException).
/// </remarks>
internal sealed class Db2ExceptionTranslator : IDbExceptionTranslator
{
    // IBM.Data.Db2's DB2Exception does not populate the inherited DbException.SqlState (nor its
    // own SQLState) property, and ErrorCode reports the generic COR_E_EXCEPTION HResult, not the
    // actual SQLCODE — confirmed against a live ibmcom/db2 container. The real SQLSTATE is only
    // available embedded in the message text, in one of two formats depending on whether the
    // error originates server-side or in the CLI driver itself: server-side errors (constraint
    // violations, etc.) lead with "ERROR [23502] [IBM][DB2/LINUXX8664] SQL0407N ...", while
    // client-side driver errors (e.g. CLI0114E) trail with "... SQLSTATE=22008".
    private static readonly Regex s_sqlStateFromMessage = new(
        @"(?:SQLSTATE[=:]\s*|ERROR \[)(\d{5})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception) ?? TryGetSqlStateFromMessage(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;
        var code = errorCode.HasValue ? Math.Abs(errorCode.Value) : (int?)null;

        if (string.Equals(sqlState, "23505", StringComparison.OrdinalIgnoreCase) || code == 803)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (string.Equals(sqlState, "23502", StringComparison.OrdinalIgnoreCase) || code == 407)
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (string.Equals(sqlState, "23513", StringComparison.OrdinalIgnoreCase) || code == 545)
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (string.Equals(sqlState, "23503", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sqlState, "23504", StringComparison.OrdinalIgnoreCase) ||
            code is 530 or 531 or 532)
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (string.Equals(sqlState, "40001", StringComparison.OrdinalIgnoreCase) || code is 911 or 913)
        {
            return new SerializationConflictException(
                $"{operationKind} encountered a serialization conflict on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }

    private static string? TryGetSqlStateFromMessage(Exception exception)
    {
        var match = s_sqlStateFromMessage.Match(exception.Message);
        return match.Success ? match.Groups[1].Value : null;
    }
}
