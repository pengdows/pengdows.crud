using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates IBM Informix Dynamic Server (IDS) specific exceptions into the pengdows.crud
/// exception hierarchy.
/// </summary>
/// <remarks>
/// Backported from pengdows.crud 3.0 and adapted to this 2.0.6 patch line's translator
/// signature (SupportedDatabase, not ISqlDialect - 2.0.6 predates 3.0's dialect-owned
/// constraint-kind delegation, so classification is independently re-derived here, matching
/// this branch's other translators, e.g. Db2ExceptionTranslator). Constants match
/// InformixDialect.cs's IsXxxViolation overrides exactly.
/// UNVERIFIED against a live driver exception from this session - built from IBM's
/// documentation, not a real connection. In particular: whether Informix.Net.Core's exception
/// type populates a SQLSTATE/SQLCODE property this session confirmed a name for is not yet
/// checked; detection relies on DbExceptionTranslationSupport's generic reflection-based
/// error-code extraction (checks common property names like ErrorCode/NativeError/SqlState
/// across provider exception shapes it doesn't have specific knowledge of yet).
/// Codes used:
///   23000 / -268 (logged db) / -239 (unlogged db)  unique constraint violation
///   -691 / -692  foreign key violation (insert: parent missing / delete: child exists)
///   -391  not-null violation
///   -530  check constraint violation
///   -143  deadlock
///   -244  serialization/lock conflict (closest documented analog - UNVERIFIED)
///   -908 (SQLSTATE 08004) / -27001 / -27002  connection/communication failure
/// </remarks>
internal sealed class InformixExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;
        var code = errorCode.HasValue ? Math.Abs(errorCode.Value) : (int?)null;

        if (code is 908 or 27001 or 27002 ||
            sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (string.Equals(sqlState, "23000", StringComparison.OrdinalIgnoreCase) || code is 268 or 239)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (code == 391)
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (code == 530)
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (code is 691 or 692)
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (code == 143)
        {
            return new DeadlockException(
                $"{operationKind} encountered a deadlock on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (code == 244)
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
}
