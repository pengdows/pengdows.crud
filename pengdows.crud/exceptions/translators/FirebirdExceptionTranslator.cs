using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Firebird-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: connection (SQLSTATE class 08) → serialization/deadlock conflict (SQLSTATE
/// 40001) → unique/PK constraint → FK → NOT NULL → CHECK → timeout → fallback.
/// Message-based detection is used for constraint violations because Firebird wraps ISC codes
/// inside FbException.Errors; extracting them via reflection is fragile across provider
/// versions. The violation message text is stable across Firebird 3–5. Connection failures are
/// SQLSTATE-based instead: FbException exposes a real (all-caps) "SQLSTATE" property — confirmed
/// against a live container, a closed-port connect attempt reports SQLSTATE "08006" (ANSI
/// connection-exception class) with message "Unable to complete network request to host ...".
/// <para>
/// Serialization/deadlock: confirmed against a live container that Firebird CANNOT distinguish
/// a true lock-cycle deadlock from an optimistic update conflict — BOTH a reversed-lock-order
/// two-connection scenario and a snapshot-read-then-conflicting-write scenario produced the
/// IDENTICAL signature: SQLSTATE "40001", ISC error code 335544336, and message
/// "deadlock\nupdate conflicts with concurrent update\nconcurrent transaction number is N".
/// Since there is no reliable signal to tell them apart, both are classified as
/// SerializationConflictException here — matching the same "SQLSTATE 40001 can't disambiguate"
/// precedent already used for Db2 (see Db2ExceptionTranslator/SqlDialect.TryClassifyProviderException).
/// </para>
/// </remarks>
internal sealed class FirebirdExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);

        // Checked first: a connection-refused/timed-out message could otherwise be swallowed by
        // either the constraint checks below (unlikely) or the LooksLikeTimeout heuristic (a
        // "connection timed out" OS error would literally contain the word "timeout").
        if (sqlState?.StartsWith("08", StringComparison.Ordinal) == true)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Deadlock/SerializationFailure/Timeout classification is delegated to the dialect's
        // single ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment. FirebirdDialect's
        // override checks the identical SqlState-or-message signals this translator used to check
        // directly, so this is a behavior-preserving delegation, not a narrowing. Checked before
        // constraint-kind so the ambiguous-40001 case isn't shadowed by a coincidental constraint
        // message match.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is delegated to the dialect
        // (see IDbExceptionTranslator.Translate's doc comment) and checked BEFORE
        // LooksLikeTimeout: Firebird embeds the failed key value in the exception message, and
        // key values may contain "timeout" (e.g. distributed lock resource names like
        // "lock-timeout-{guid}"), which would otherwise cause the timeout heuristic to fire and
        // swallow a legitimate PK violation.
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
