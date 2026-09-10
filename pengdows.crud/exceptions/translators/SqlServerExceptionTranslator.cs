using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class SqlServerExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        // Check unique violation first (delegated to the dialect — see IDbExceptionTranslator's
        // doc comment) so that PK-violation messages that happen to contain the word "timeout" in
        // their payload (e.g. a distributed-lock resource named "lock-timeout-<guid>") are not
        // mis-classified as CommandTimeoutException.
        if (exception is DbException dbEx0 && dialect.IsUniqueViolation(dbEx0))
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        // Deadlock (1205)/SerializationFailure (3960)/Timeout (-2, or the generic LooksLikeTimeout
        // heuristic) classification is delegated to the dialect's single ClassifyException/
        // TryClassifyProviderException source — see DbExceptionTranslationSupport.
        // TryCreateFromCategory's doc comment. Checked here (before Connection, matching this
        // method's original ordering) so a PK-violation message containing "timeout" in its
        // payload is still caught by the uniqueness check above first.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
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

        if (exception is DbException dbEx)
        {
            if (dialect.IsNotNullViolation(dbEx))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

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
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
