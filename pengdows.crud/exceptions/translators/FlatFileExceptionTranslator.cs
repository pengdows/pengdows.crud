using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates pengdows.flatfile-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: timeout (HYT00) / read-only violation (25006), delegated to the dialect →
/// connection failure (SQLSTATE class 08: 08001 missing location, 08004 writer refused) →
/// constraint kind (Unique/ForeignKey/NotNull/Check, delegated to FlatFileDialect.IsXxxViolation,
/// which reads FlatFileException.SqlState) → fallback. pengdows.flatfile's other DDL/file errors
/// (missing table, malformed data file, etc.) are plain InvalidOperationException, not
/// DbException, so they never reach a translator — see pengdows.flatfile's CLAUDE.md.
/// </remarks>
internal sealed class FlatFileExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;

        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        // 08001 (database location or data file missing) and 08004 (writer refused: another
        // process holds the database) - pengdows.flatfile 0.2.1-preview.1+.
        if (DbExceptionTranslationSupport.TryGetSqlState(exception)?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);

        if (DbExceptionTranslationSupport.TryCreateConstraintViolation(dialect, exception, database, operationKind) is { } violation)
        {
            return violation;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
