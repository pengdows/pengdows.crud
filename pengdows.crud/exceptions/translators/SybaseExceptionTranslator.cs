using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Sybase (SAP) ASE errors, surfaced via AdoNetCore.AseClient's <c>AseException</c>.
/// </summary>
/// <remarks>
/// <c>AseException</c> does not derive from <see cref="System.Data.Common.DbException"/> and has no
/// top-level error-code property; the real error number lives on <c>AseException.Errors[0].MessageNumber</c>.
/// <see cref="DbExceptionTranslationSupport.TryGetErrorCode"/> reflects over that "Errors" collection as a
/// fallback specifically to support this shape. ASE's message numbers for these constraint families are
/// distinct from SQL Server's (which reuses 547 for both foreign-key and check violations) — ASE assigns
/// foreign-key and check violations separate numbers, so no message-text discrimination is needed here.
/// <para>
/// Deliberately NOT delegated to <see cref="ISqlDialect"/>'s constraint-kind checks (unlike every other
/// translator — see <see cref="IDbExceptionTranslator"/>'s doc comment): <c>SybaseDialect</c> only
/// overrides the <c>Exception</c>-typed <c>IsUniqueViolation</c> override, not
/// <c>IsForeignKeyViolation</c>/<c>IsNotNullViolation</c>/<c>IsCheckConstraintViolation</c> (which would
/// silently fall back to <see cref="ISqlDialect"/>'s <c>false</c>-returning defaults since
/// <c>AseException</c> isn't a <c>DbException</c>). This translator's own complete, distinct-error-code-
/// per-constraint-type classification stays authoritative until the dialect surface is widened to match.
/// </para>
/// </remarks>
internal sealed class SybaseExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);

        // Check specific error codes first so that PK-violation messages that happen to contain
        // the word "timeout" are not mis-classified as CommandTimeoutException (see SqlServer's
        // equivalent regression note).
        if (errorCode == 2601)
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName);
        }

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception) || errorCode == -2)
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return errorCode switch
        {
            233 => new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            548 => new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            546 => new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            1205 => new DeadlockException(
                $"{operationKind} deadlocked on {database}: {exception.Message}",
                database, exception, sqlState, errorCode, constraintName),
            _ => DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind)
        };
    }
}
