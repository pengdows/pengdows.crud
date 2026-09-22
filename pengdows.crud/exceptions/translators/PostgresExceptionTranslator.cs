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

        DatabaseException? typed = sqlState switch
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
            _ => null
        };

        if (typed != null)
        {
            return typed;
        }

        // Spanner returns SqlState "P0001" (a generic raise-exception code) for NotNull/Check/
        // delete-side ForeignKey violations, not the ANSI class-23 codes real PostgreSQL uses for
        // those three (unique and insert-side foreign-key violations DO use the real "23505"/
        // "23503" codes and are already caught above) — verified live against a real Spanner
        // Omni + PGAdapter instance. Message-pattern matching is the only reliable signal here.
        // Kept in sync with SpannerDialect's own identical IsXxxViolation overrides (used by the
        // separate advisory ClassifyException path) - this branch's translators don't delegate to
        // the dialect the way 3.0's do, so the same three patterns are necessarily duplicated here.
        if (database == SupportedDatabase.Spanner)
        {
            var message = exception.Message;
            if (message.Contains("must not be NULL", StringComparison.OrdinalIgnoreCase))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (message.Contains("Check constraint", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("is violated", StringComparison.OrdinalIgnoreCase))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (message.Contains("Foreign key constraint violation", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("referenced row", StringComparison.OrdinalIgnoreCase))
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
