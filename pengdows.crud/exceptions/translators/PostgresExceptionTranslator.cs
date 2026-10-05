using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal sealed class PostgresExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        // Deadlock (40P01)/SerializationFailure (40001)/AmbiguousResult (40003, CockroachDB's real
        // "result is ambiguous" error, kept in this shared translator since it's inert for
        // PostgreSql/AuroraPostgreSql)/Timeout (55P03 lock_not_available, 57014 query_canceled, or
        // the generic LooksLikeTimeout heuristic) classification is delegated to the dialect's
        // single ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment. Deliberately not
        // SerializationConflictException for 40003 — retry is not automatically safe there (see
        // AmbiguousResultException's own remarks).
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        // 53300 too_many_connections: the server refused the connection at its connection limit
        // (confirmed live, "sorry, too many clients already"). The rest of class 53 is resource
        // exhaustion inside a working session, not a connection failure.
        if (sqlState == "53300")
        {
            return DbExceptionTranslationSupport.CreateTooManyConnections(database, exception, operationKind);
        }

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is single-sourced from the
        // dialect — see IDbExceptionTranslator.Translate's doc comment.
        if (DbExceptionTranslationSupport.TryCreateConstraintViolation(dialect, exception, database, operationKind) is { } violation)
        {
            return violation;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
