using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class MySqlExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        // Check unique constraint codes before LooksLikeTimeout so that PK-violation messages
        // that happen to contain "timeout" in their payload (e.g. a distributed-lock resource
        // named "lock-timeout-<guid>") are not mis-classified as CommandTimeoutException.
        if (errorCode is 1062 or 1169)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception) || errorCode == 1205)
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        if (errorCode is 1040 or 1042 or 1043 or 1044)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (errorCode == 4025 ||
            (message.Contains("constraint", StringComparison.OrdinalIgnoreCase) &&
             message.Contains("failed for", StringComparison.OrdinalIgnoreCase)))
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        return errorCode switch
        {
            1216 or 1451 or 1452 => new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            1048 => new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            3819 => new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            1213 => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
