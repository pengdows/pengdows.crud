using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates SAP HANA-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Backported from pengdows.crud 3.0 and adapted to this 2.0.6 patch line's translator
/// signature (SupportedDatabase, not ISqlDialect - 2.0.6 predates 3.0's dialect-owned
/// constraint-kind/category delegation, so every classification is independently re-derived
/// here, matching this branch's other translators, e.g. Db2ExceptionTranslator). Codes match
/// HanaDialect.cs's IsXxxViolation overrides and SqlDialect.cs's SupportedDatabase.SapHana
/// TryClassifyProviderException case exactly.
/// CONFIRMED live against a real Sap.Data.Hana.HanaException thrown by a saplabs/hanaexpress
/// container (see HanaDialect.cs's file-level summary for the full research trail).
/// Critically, HanaException.SqlState is an empty string for every violation kind except unique
/// (which the driver leaves at "23000"), and HanaException.ErrorCode is *always* the generic COM
/// HRESULT -2147467259 regardless of violation kind — this translator does not use either.
/// The only reliable discriminator is HanaException.NativeError, found via
/// DbExceptionTranslationSupport's generic reflection-based error-code extraction.
/// Codes used: 301 unique, 461/462 foreign key (insert/update parent missing, delete/update
/// child exists), 287 not-null, 677 check, 133 deadlock (SAP KBA 1999998/2658020, not
/// live-reproduced), 131 lock-wait timeout (same KBA, not live-reproduced), 129 read-only
/// violation (CONFIRMED live).
/// </remarks>
internal sealed class HanaExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        if (errorCode == 301)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 287)
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 677)
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode is 461 or 462)
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 133)
        {
            return new DeadlockException(
                $"{operationKind} encountered a deadlock on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 131)
        {
            return new CommandTimeoutException(
                $"{operationKind} timed out waiting for a lock on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (errorCode == 129)
        {
            return DbExceptionTranslationSupport.CreateReadOnlyViolation(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
