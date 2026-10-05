using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Oracle-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Detection order: ORA error code → timeout → fallback.
/// ORA error codes are checked first because Oracle 23c includes column values in
/// extended error messages (ORA-03301), which may contain user data such as "timeout"
/// and would otherwise trigger a false-positive timeout classification.
/// Oracle exception codes used:
///   ORA-00001  unique constraint violated
///   ORA-01400  cannot insert NULL
///   ORA-02290  check constraint violated
///   ORA-02291  integrity constraint violated - parent key not found (FK insert)
///   ORA-02292  integrity constraint violated - child record found (FK delete)
///   ORA-00060  deadlock detected
///   ORA-08177  can't serialize access for this transaction
///   ORA-50201  Oracle Communication: failed to connect to server (confirmed against a live
///     ODP.NET connect attempt to a closed port — reports OracleException.Number == 50201).
/// </remarks>
internal sealed class OracleExceptionTranslator : IDbExceptionTranslator
{
    private static bool ListenerAtCapacity(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("ORA-12516", StringComparison.Ordinal) ||
                message.Contains("ORA-12519", StringComparison.Ordinal) ||
                message.Contains("ORA-12520", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        // 2391: SESSIONS_PER_USER limit (confirmed live). 18/20: the documented sessions/processes
        // limits (not reproduced live).
        if (errorCode is 2391 or 18 or 20)
        {
            return DbExceptionTranslationSupport.CreateTooManyConnections(database, exception, operationKind);
        }

        // 50201: ODP.NET could not connect. The server-wide 'processes' limit also surfaces as this
        // (wrapping ORA-12537, confirmed live), but so does any other failed connect, so it stays a
        // plain connection failure.
        // Confirmed live 2026-10-04 (full integration run): with the server's process slots in use it
        // wraps "ORA-12516: ... Listener ... does not have a protocol handler ... ready"; ORA-12516/
        // 12519/12520 are the listener's no-free-handler errors, i.e. the server is at capacity.
        if (errorCode == 50201)
        {
            return ListenerAtCapacity(exception)
                ? DbExceptionTranslationSupport.CreateTooManyConnections(database, exception, operationKind)
                : DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // Constraint-kind classification (Unique/FK/NotNull/Check) is delegated to the dialect —
        // see IDbExceptionTranslator.Translate's doc comment.
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

        // Deadlock (60)/SerializationFailure (8177)/Timeout classification is delegated to the
        // dialect's single ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
