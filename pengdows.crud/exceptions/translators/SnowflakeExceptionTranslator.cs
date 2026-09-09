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

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // NOT NULL is the one constraint Snowflake actually enforces at runtime — delegated to
        // the dialect (see IDbExceptionTranslator.Translate's doc comment). Unique/FK/Check are
        // not checked here at all: SnowflakeDialect.IsUniqueViolation/IsForeignKeyViolation/
        // IsCheckConstraintViolation are hardcoded false (Snowflake parses but never enforces
        // those constraint types), so there is nothing to delegate for them.
        if (exception is DbException dbEx && dialect.IsNotNullViolation(dbEx))
        {
            var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
            var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
