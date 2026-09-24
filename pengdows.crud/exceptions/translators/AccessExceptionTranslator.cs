using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Microsoft Access (Jet/ACE) exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// <c>OleDbException</c> IS a real <see cref="DbException"/> (unlike Sybase's <c>AseException</c>,
/// the one deliberately-documented exception to this unification — see
/// <c>SybaseExceptionTranslator</c>'s remarks), so this translator follows the standard unified
/// pattern: constraint-kind classification (Unique/FK/NotNull/Check) is delegated entirely to
/// <c>AccessDialect</c>'s <c>IsXxxViolation</c> overrides rather than re-derived here.
/// <para>
/// CONFIRMED live against a real <c>.accdb</c> (both ACE 12.0 and ACE 16.0): <c>OleDbException</c>
/// reports the identical generic COM HRESULT (<c>ErrorCode = -2147467259</c>) and an empty
/// <c>Errors</c> collection for every constraint-violation kind — UNIQUE, NOT NULL, CHECK, FK, and
/// PK-duplicate alike. There is no numeric or SQLSTATE signal available at all; classification is
/// pure English message-text substring matching, same as Firebird's approach. Connection-level
/// failures are classified the same way, since no SqlState is ever populated to check instead —
/// see the "Could not find file"/"already opened by user" checks below, both captured live
/// against a real .accdb (a missing file, and a file another connection/process holds
/// exclusively).
/// </para>
/// </remarks>
internal sealed class AccessExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);

        // Connection-level failures: no SqlState exists to check (unlike most other translators'
        // SqlState-"08"-class checks) — OleDbException never populates one for Access — so this
        // is message-substring based, same shape as the constraint checks below. Both messages
        // captured live this session: a missing .accdb file, and a .accdb another
        // connection/process already holds exclusively. Checked first, before any other
        // classification, since a connection that never opened at all isn't a constraint
        // violation or a lock-wait condition.
        if (message.Contains("Could not find file", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("already opened by user", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Deadlock/SerializationFailure/Timeout classification is delegated to the dialect's
        // single ClassifyException/TryClassifyProviderException source (see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment, and
        // AccessDialect.TryClassifyProviderException's "currently locked" -> Timeout case, found
        // live this session) — checked before constraint-kind so a lock-wait message is never
        // shadowed by a coincidental constraint-message match (none of the four constraint
        // messages contain "currently locked", but this also matches every other unified
        // translator's ordering, e.g. FirebirdExceptionTranslator).
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        // Constraint-kind classification delegated to the dialect (see this file's remarks and
        // IDbExceptionTranslator.Translate's doc comment) — checked before LooksLikeTimeout,
        // consistent with every other unified translator, since a constraint message could
        // incidentally contain wording a timeout heuristic might otherwise match.
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

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
