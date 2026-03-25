using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Oracle-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: timeout → ORA error code → fallback.
/// Oracle exception codes used:
///   ORA-00001  unique constraint violated
///   ORA-01400  cannot insert NULL
///   ORA-02290  check constraint violated
///   ORA-02291  integrity constraint violated - parent key not found (FK insert)
///   ORA-02292  integrity constraint violated - child record found (FK delete)
///   ORA-00060  deadlock detected
///   ORA-08177  can't serialize access for this transaction
/// </remarks>
internal sealed class OracleExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        return errorCode switch
        {
            1 => new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            1400 => new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            2290 => new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            2291 or 2292 => new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            60 => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            8177 => new SerializationConflictException(
                $"{operationKind} encountered a serialization conflict on {database}: {message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
