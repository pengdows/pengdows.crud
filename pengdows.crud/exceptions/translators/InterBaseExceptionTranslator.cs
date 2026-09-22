using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates InterBase-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Backported from pengdows.crud 3.0 and adapted to this 2.0.6 patch line's translator
/// signature (SupportedDatabase, not ISqlDialect - 2.0.6 predates 3.0's dialect-owned
/// constraint-kind delegation, so classification is independently re-derived here, matching
/// this branch's other translators). Constants match InterBaseDialect.cs's IsXxxViolation
/// overrides exactly.
/// CONFIRMED live against a real InterBaseSql.Data.InterBaseClient.IBException thrown by a real
/// InterBase 15 server (see InterBaseDialect.cs's file-level summary for the full research
/// trail). Unlike HANA, IBException.ErrorCode reliably carries InterBase's real ISC status code
/// (confirmed by enumerating IBException's public properties live — it has no "Number"/
/// "SqliteErrorCode"/"NativeError" property to shadow it, so DbExceptionTranslationSupport's
/// reflection probe falls through to the base DbException.ErrorCode correctly).
/// No deadlock/lock-timeout codes are classified here — deliberately left unclassified, not an
/// oversight, since neither was reproduced live (would require two contending sessions); see
/// InterBaseDialect.cs's file-level summary.
/// Codes used: 335544665 unique/PK, 335544466 foreign key (both directions), 335544347 not-null,
/// 335544558 check.
/// </remarks>
internal sealed class InterBaseExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        if (errorCode == 335544665)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 335544347)
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 335544558)
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 335544466)
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
