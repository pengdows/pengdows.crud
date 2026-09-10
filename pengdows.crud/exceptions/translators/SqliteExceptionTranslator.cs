using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates SQLite-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: timeout/read-only violation (delegated to the dialect) →
/// connection (SQLITE_CANTOPEN/SQLITE_NOTADB) →
/// unique/PK constraint → check constraint → not-null → foreign-key → fallback.
/// Error codes are extracted via reflection on the <c>SqliteException.SqliteErrorCode</c>
/// property (Microsoft.Data.Sqlite), so this translator works without a hard reference
/// to the SQLite driver assembly. Constraint-kind classification is delegated to
/// SqliteDialect.IsXxxViolation (see IDbExceptionTranslator.Translate's doc comment) —
/// this translator previously used a bare message-substring check here that could
/// disagree with the dialect's extended-result-code-based check (see
/// SqliteTranslatorTests.MessageWithIncidentalUniqueSubstring_AgreesWithDialectClassification).
/// </remarks>
internal sealed class SqliteExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;

        // Timeout (generic LooksLikeTimeout heuristic) and ReadOnlyViolation (SQLITE_READONLY = 8)
        // classification is delegated to the dialect's single ClassifyException/
        // TryClassifyProviderException source — see DbExceptionTranslationSupport.
        // TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);

        // SQLITE_CANTOPEN = 14, SQLITE_NOTADB = 26
        if (errorCode is 14 or 26)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return new UniqueConstraintViolationException(
                    $"{operationKind} violated a unique constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return new ForeignKeyViolationException(
                    $"{operationKind} violated a foreign key constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return new NotNullViolationException(
                    $"{operationKind} violated a not-null constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return new CheckConstraintViolationException(
                    $"{operationKind} violated a check constraint on {database}: {message}",
                    database, exception, errorCode: errorCode);
            }
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
