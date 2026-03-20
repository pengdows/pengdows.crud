using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class SqlServerExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception) || errorCode == -2)
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        if (errorCode is 10053 or 10054 or 10060 or 233 or 10061)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        var msg = exception.Message;
        if (msg.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
            (msg.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("broken", StringComparison.OrdinalIgnoreCase)))
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        return errorCode switch
        {
            2601 or 2627 => new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            515 => new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            547 when exception.Message.Contains("CHECK", StringComparison.OrdinalIgnoreCase) =>
                new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName),
            547 => new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            1205 => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
