using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Db2 (Db2 for Linux/Unix/Windows) specific exceptions into the pengdows.crud
/// exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: SQLSTATE (ANSI-standard, primary) → SQLCODE magnitude (fallback,
/// sign-tolerant since it is not yet confirmed whether IBM.Data.Db2's DB2Exception reports
/// SQLCODE as its native negative value or an unsigned magnitude — verify against a live
/// driver exception in Phase 2) → timeout → fallback.
/// Db2 SQLSTATE/SQLCODE pairs used:
///   23505 / -803  unique constraint (index) violation
///   23503 / -530, -531, -532  foreign key (referential integrity) violation on insert/update
///   23504 / -532  foreign key (referential integrity) violation on delete (RESTRICT)
///   23502 / -407  not-null violation
///   23513 / -545  check constraint violation
///   40001 / -911, -913  deadlock or lock timeout (SQLSTATE 40001 cannot by itself
///     distinguish the two on Db2 — treated as SerializationConflictException here,
///     matching the classification used in SqlDialect.TryClassifyProviderException).
///   08xxx  connection exception class (e.g. 08001 SQLCODE -30081 "communication error") —
///     confirmed against a live ibmcom/db2 container: a closed-port connect attempt reports
///     "ERROR [08001] [IBM] SQL30081N ... SQLSTATE=08001".
/// </remarks>
internal sealed class Db2ExceptionTranslator : IDbExceptionTranslator
{
    // IBM.Data.Db2's DB2Exception does not populate the inherited DbException.SqlState (nor its
    // own SQLState) property, and ErrorCode reports the generic COR_E_EXCEPTION HResult, not the
    // actual SQLCODE — confirmed against a live ibmcom/db2 container. The real SQLSTATE is only
    // available embedded in the message text (leading "ERROR [nnnnn]" or trailing
    // "SQLSTATE=nnnnn" — DbExceptionTranslationSupport.TryGetSqlState handles both forms).
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
        // see IDbExceptionTranslator.Translate's doc comment. Db2Dialect's overrides check the
        // identical SqlState-or-numeric-SQLCODE-magnitude signals this translator used to check
        // directly, so this is a behavior-preserving delegation, not a narrowing.
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
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment. Db2Dialect's override
        // checks the identical SqlState-or-SQLCODE-magnitude signals this translator used to check
        // directly, so this is a behavior-preserving delegation, not a narrowing.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
