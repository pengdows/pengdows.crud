using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class MySqlExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        // Check unique constraint (delegated to the dialect) before LooksLikeTimeout so that
        // PK-violation messages that happen to contain "timeout" in their payload (e.g. a
        // distributed-lock resource named "lock-timeout-<guid>") are not mis-classified as
        // CommandTimeoutException.
        if (exception is DbException dbEx0 && dialect.IsUniqueViolation(dbEx0))
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        // Deadlock (1213)/Timeout (1205, or the generic LooksLikeTimeout heuristic)/
        // SerializationFailure classification is delegated to the dialect's single
        // ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment. Checked here (before
        // Connection, matching this method's original ordering) so a PK-violation message
        // containing "timeout" in its payload is still caught by the uniqueness check above first.
        // MySqlDialect.TryClassifyProviderException also recognizes SqlState "40001" as
        // SerializationFailure — genuinely new behavior for this translator (previously
        // unreachable here, since 1213's own SqlState is already "40001" and errorCode is checked
        // first), not just a refactor: any future MySQL-family error that carries SqlState 40001
        // without errorCode 1213 now correctly becomes SerializationConflictException instead of
        // falling through to the generic fallback.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        if (errorCode is 1040 or 1042 or 1043 or 1044)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (exception is DbException dbEx)
        {
            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName);
            }
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
