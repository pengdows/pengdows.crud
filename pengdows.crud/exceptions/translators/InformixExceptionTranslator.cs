using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates IBM Informix Dynamic Server (IDS) specific exceptions into the pengdows.crud
/// exception hierarchy.
/// </summary>
/// <remarks>
/// UNVERIFIED against a live driver exception from this session — built from IBM's
/// documentation (see InformixDialect.cs's file-level summary for full citations), not a real
/// connection. In particular: whether Informix.Net.Core's exception type populates a
/// SQLSTATE/SQLCODE property this session confirmed a name for is not yet checked; the
/// constraint-kind and connection-failure detection below relies entirely on the same numeric
/// error codes InformixDialect's IsXxxViolation overrides already use (delegated, not
/// re-derived — see IDbExceptionTranslator.Translate's own doc comment for why this
/// delegation pattern exists), reached via DbExceptionTranslationSupport's generic
/// reflection-based error-code extraction (checks common property names like ErrorCode/
/// NativeError/SqlState across provider exception shapes it doesn't have specific knowledge
/// of yet).
/// Codes used (all cited on InformixDialect.cs):
///   23000 / -268 (logged db) / -239 (unlogged db)  unique constraint violation
///   -691 / -692  foreign key violation (insert: parent missing / delete: child exists)
///   -391  not-null violation
///   -530  check constraint violation
///   -908 (SQLSTATE 08004) / -27001 / -27002  connection/communication failure
/// </remarks>
internal sealed class InformixExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is delegated to the dialect —
        // see IDbExceptionTranslator.Translate's doc comment. InformixDialect's overrides check
        // the same numeric error codes this translator would otherwise re-derive.
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

        // Deadlock/SerializationFailure/Timeout classification is delegated to the dialect's
        // single ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
