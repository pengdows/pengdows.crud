using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Oracle-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: ORA error code → timeout → fallback.
/// ORA error codes are checked first because Oracle 23c includes column values in
/// extended error messages (ORA-03301), which may contain user data such as "timeout"
/// and would otherwise trigger a false-positive timeout classification.
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
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        switch (errorCode)
        {
            case 1:
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            case 1400:
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            case 2290:
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            case 2291:
            case 2292:
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            case 60:
                return new DeadlockException(
                    $"{operationKind} deadlocked on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            case 8177:
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
