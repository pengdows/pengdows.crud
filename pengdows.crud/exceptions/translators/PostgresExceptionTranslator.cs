using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class PostgresExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        if (database == SupportedDatabase.Snowflake)
        {
            return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
        }

        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception) ||
            (sqlState == "57014" && exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        return sqlState switch
        {
            "23505" => new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "23503" => new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "23502" => new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "23514" => new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "40P01" => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "40001" => new SerializationConflictException(
                $"{operationKind} hit a serialization conflict on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
