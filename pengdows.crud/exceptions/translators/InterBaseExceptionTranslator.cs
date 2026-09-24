using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates InterBase-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// CONFIRMED live against a real InterBaseSql.Data.InterBaseClient.IBException thrown by a real
/// InterBase 15 server (see InterBaseDialect.cs's file-level summary for the full research
/// trail). Unlike HANA, IBException.ErrorCode reliably carries InterBase's real ISC status code
/// (confirmed by enumerating IBException's public properties live — it has no "Number"/
/// "SqliteErrorCode"/"NativeError" property to shadow it, so DbExceptionTranslationSupport's
/// reflection probe falls through to the base DbException.ErrorCode correctly). Constraint-kind
/// classification (Unique/FK/NotNull/Check) is delegated entirely to InterBaseDialect's
/// IsXxxViolation overrides.
/// </remarks>
internal sealed class InterBaseExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, sqlState, errorCode, constraintName);
            }
        }

        // Deadlock/Timeout classification is delegated to the dialect's single
        // ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
