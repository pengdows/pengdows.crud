using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates Microsoft Access (Jet/ACE) exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// Backported from pengdows.crud 3.0 and adapted to this 2.0.6 patch line's translator
/// signature (SupportedDatabase, not ISqlDialect — 2.0.6 predates 3.0's dialect-owned
/// constraint-kind delegation, so classification is independently re-derived here, matching this
/// branch's other translators, e.g. InformixExceptionTranslator/DuckDbExceptionTranslator).
/// Message substrings match AccessDialect.cs's IsXxxViolation overrides exactly.
/// <para>
/// CONFIRMED live against a real <c>.accdb</c> (both ACE 12.0 and ACE 16.0 — see
/// <c>AccessDialect.cs</c>'s file-level AI SUMMARY): <c>OleDbException</c> reports the identical
/// generic COM HRESULT (<c>ErrorCode = -2147467259</c>) and an empty <c>Errors</c> collection for
/// every constraint-violation kind — UNIQUE, NOT NULL, CHECK, FK, and PK-duplicate alike. There is
/// no numeric or SQLSTATE signal available at all; classification is pure English message-text
/// substring matching, same as Firebird's approach. Connection-level failures are classified the
/// same way, since no SqlState is ever populated to check instead — see the "Could not find
/// file"/"already opened by user" checks below, both captured live against a real .accdb (a
/// missing file, and a file another connection/process holds exclusively).
/// </para>
/// </remarks>
internal sealed class AccessExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        var message = exception.Message;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);

        // Connection-level failures: no SqlState exists to check (unlike most other translators'
        // SqlState-"08"-class checks) — OleDbException never populates one for Access — so this
        // is message-substring based, same shape as the constraint checks below. Both messages
        // captured live: a missing .accdb file, and a .accdb another connection/process already
        // holds exclusively. Checked first, before any other classification, since a connection
        // that never opened at all isn't a constraint violation or a lock-wait condition.
        if (message.Contains("Could not find file", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("already opened by user", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        // CONFIRMED live: a second connection writing to a row/page held by another connection's
        // open transaction blocks, then fails outright with this exact message once contention
        // resolves — a genuine lock-WAIT scenario, not a detected circular-wait deadlock. Checked
        // before constraint-kind so a lock-wait message is never shadowed by a coincidental
        // constraint-message match (none of the four constraint messages below contain "currently
        // locked").
        if (message.Contains("currently locked", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        // CONFIRMED live: the exact message a real ACE connection opened with "Mode=Read"
        // (AccessDialect.GetReadOnlyConnectionParameter) returns when a write is attempted
        // against it — mirrors SqliteExceptionTranslator/DuckDbExceptionTranslator's
        // ReadOnlyViolation classification.
        if (message.Contains("must use an updateable query", StringComparison.OrdinalIgnoreCase))
        {
            return DbExceptionTranslationSupport.CreateReadOnlyViolation(database, exception, operationKind);
        }

        // Constraint-kind classification — message substrings match AccessDialect's own
        // IsXxxViolation overrides exactly (see that file's remarks for why pure message-text
        // matching is the only option here: OleDbException carries no discriminable numeric error
        // code for any violation kind).
        if (message.Contains("duplicate values in the index, primary key, or relationship",
                StringComparison.OrdinalIgnoreCase))
        {
            return new UniqueConstraintViolationException(
                $"{operationKind} violated a unique constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        // Covers both real message shapes confirmed live: INSERT blocked by a missing parent row
        // ("a related record is required") and DELETE blocked by an existing child row ("includes
        // related records") — "related record" is a substring of both.
        if (message.Contains("related record", StringComparison.OrdinalIgnoreCase))
        {
            return new ForeignKeyViolationException(
                $"{operationKind} violated a foreign key constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("must enter a value", StringComparison.OrdinalIgnoreCase))
        {
            return new NotNullViolationException(
                $"{operationKind} violated a not-null constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (message.Contains("prohibited by the validation rule", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckConstraintViolationException(
                $"{operationKind} violated a check constraint on {database}: {message}",
                database, exception, errorCode: errorCode);
        }

        if (DbExceptionTranslationSupport.LooksLikeTimeout(exception))
        {
            return DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind);
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
