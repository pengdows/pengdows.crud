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

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception) ||
            (sqlState == "57014" && exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is single-sourced from the
        // dialect — see IDbExceptionTranslator.Translate's doc comment.
        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
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

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                    database, exception, sqlState, errorCode, constraintName);
            }
        }

        return sqlState switch
        {
            "40P01" => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            "40001" => new SerializationConflictException(
                $"{operationKind} hit a serialization conflict on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            // See the matching comment in SqlDialect.cs's classification switch: 40003 is
            // CockroachDB's real "result is ambiguous" error (kept in this shared translator
            // since it's inert, not wrong, for PostgreSql/AuroraPostgreSql). Deliberately NOT
            // SerializationConflictException -- retry is not automatically safe here.
            "40003" => new AmbiguousResultException(
                $"{operationKind} result is ambiguous on {database} (commit outcome unknown): {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
