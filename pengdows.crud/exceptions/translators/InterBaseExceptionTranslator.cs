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

        // 335544744 isc_max_att_exceeded, "Maximum user count exceeded": the license's concurrent
        // user cap (confirmed live on InterBase 15 Developer Edition at 16 attachments).
        if (errorCode == 335544744)
        {
            return DbExceptionTranslationSupport.CreateTooManyConnections(database, exception, operationKind);
        }

        // 335544721 isc_network_error, "Unable to complete network request to host": the server
        // is unreachable or the connection dropped (confirmed live for a stopped server, a wrong
        // port and a server restart under an open connection). The provider sets no SQLSTATE, so
        // Firebird's class-08 check doesn't apply here.
        if (errorCode == 335544721)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.Unique, database, exception, operationKind);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.NotNull, database, exception, operationKind);
            }

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.Check, database, exception, operationKind);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.ForeignKey, database, exception, operationKind);
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
