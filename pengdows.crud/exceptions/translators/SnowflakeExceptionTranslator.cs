using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Snowflake-specific exceptions.
/// </summary>
/// <remarks>
/// Snowflake parses UNIQUE/PRIMARY KEY/FOREIGN KEY/CHECK constraint DDL but does not enforce any
/// of them at runtime (see <c>SnowflakeDialect.EnforcesConstraints</c>,
/// <c>EnforcesForeignKeyConstraints</c>, <c>SupportsUniqueConstraints</c>,
/// <c>SupportsCheckConstraints</c> — all false), so those exception types structurally cannot
/// occur. NOT NULL is the one constraint Snowflake actually enforces (error 100072, SQLSTATE
/// 23502, message "NULL result in a non-nullable column" per Snowflake's own error catalog).
/// </remarks>
internal sealed class SnowflakeExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;

        // Timeout classification is delegated to the dialect's single ClassifyException/
        // TryClassifyProviderException source (Snowflake has no dialect-specific
        // Deadlock/SerializationFailure signal, so this only ever resolves via the generic
        // LooksLikeTimeout fallback inside ClassifyException itself) — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Constraint kinds are delegated to the dialect (see IDbExceptionTranslator.Translate's doc
        // comment): NOT NULL on every table, and PRIMARY KEY/UNIQUE/FOREIGN KEY, which hybrid tables
        // enforce (REV-065). Snowflake never enforces CHECK.
        if (exception is DbException dbEx)
        {
            var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
            var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
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
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
