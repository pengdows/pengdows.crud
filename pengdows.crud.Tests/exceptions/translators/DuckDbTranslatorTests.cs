using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class DuckDbTranslatorTests
{
    private readonly DuckDbExceptionTranslator _translator = new();

    // ── SQLSTATE-based detection ──────────────────────────────────────────────

    [Theory]
    [InlineData("23505")]
    public void UniqueViolation_BySqlState_Maps_UniqueConstraintViolationException(string sqlState)
    {
        var raw = new SqlStateDbException(sqlState, "Constraint Error: Duplicate key 'x' violates unique constraint 'pk'");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ForeignKey_BySqlState_23503_Maps_ForeignKeyViolationException()
    {
        var raw = new SqlStateDbException("23503", "Constraint Error: Violates foreign key constraint");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void NotNull_BySqlState_23502_Maps_NotNullViolationException()
    {
        var raw = new SqlStateDbException("23502", "Constraint Error: NOT NULL constraint failed: jobs.name");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void Check_BySqlState_23514_Maps_CheckConstraintViolationException()
    {
        var raw = new SqlStateDbException("23514", "Constraint Error: CHECK constraint failed: jobs");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    // ── Message-based fallback (no SqlState) ─────────────────────────────────

    [Theory]
    [InlineData("Duplicate key 'x' violates unique constraint 'pk'")]
    [InlineData("Constraint Error: Duplicate key violates primary key constraint")]
    public void UniqueViolation_ByMessage_Maps_UniqueConstraintViolationException(string message)
    {
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ForeignKey_ByMessage_Maps_ForeignKeyViolationException()
    {
        var raw = new SqliteMessageDbException("Violates foreign key constraint because key does not exist");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void NotNull_ByMessage_Maps_NotNullViolationException()
    {
        var raw = new SqliteMessageDbException("NOT NULL constraint failed: jobs.name");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void Check_ByMessage_Maps_CheckConstraintViolationException()
    {
        var raw = new SqliteMessageDbException("CHECK constraint failed: jobs");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    // ── Detection order: SQLSTATE / message wins over timeout keyword ─────────

    [Fact]
    public void SqlState23505_WithTimeoutKeywordInMessage_ClassifiesBySqlStateNotTimeout()
    {
        // DuckDB error messages include the violating row values; a value containing "timeout"
        // must not be misclassified as CommandTimeoutException when SQLSTATE is present.
        var raw = new SqlStateDbException("23505", "Constraint Error: Duplicate key 'timeout_value' violates unique constraint 'pk'");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void UniqueViolationMessage_WithTimeoutKeyword_ClassifiesByPatternNotTimeout()
    {
        // Same scenario using the message-pattern fallback (no SqlState populated).
        var raw = new SqliteMessageDbException("Duplicate key 'session_timeout' violates unique constraint 'pk_sessions'");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    // ── Passthrough cases ─────────────────────────────────────────────────────

    [Fact]
    public void Timeout_Maps_CommandTimeoutException()
    {
        var raw = new SqliteMessageDbException("connection timeout waiting for lock");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<CommandTimeoutException>(result);
    }

    [Fact]
    public void UnknownError_Maps_DatabaseOperationException()
    {
        var raw = new SqliteMessageDbException("some unexpected database error");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
        Assert.IsNotType<ForeignKeyViolationException>(result);
    }

    // ── Read-only violation detection ─────────────────────────────────────────

    [Fact]
    public void DuckDb_SqlState25006_MapsTo_ReadOnlyViolationException()
    {
        var raw = new SqlStateDbException("25006", "Cannot execute statement of type 'INSERT' in read-only transaction");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ReadOnlyViolationException>(result);
    }

    [Theory]
    [InlineData("Binder Error: Cannot execute statement of type \"INSERT\" on database \"mydb\" which is attached in read-only mode!")]
    [InlineData("Binder Error: Cannot execute statement of type \"UPDATE\" on database \"mydb\" which is attached in read-only mode!")]
    [InlineData("Binder Error: Cannot execute statement of type \"DELETE\" on database \"mydb\" which is attached in read-only mode!")]
    [InlineData("Attempting to execute an unsupported query in read-only transaction")]
    [InlineData("Cannot write to read-only database")]
    public void DuckDb_ReadOnlyMessage_MapsTo_ReadOnlyViolationException(string message)
    {
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ReadOnlyViolationException>(result);
    }

    [Fact]
    public void DuckDb_ReadOnlyViolation_IsNotTransient()
    {
        var raw = new SqlStateDbException("25006", "read-only transaction");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ReadOnlyViolationException>(result);
        Assert.Equal(false, result.IsTransient);
    }

    [Fact]
    public void DuckDb_ReadOnlyMessage_WithTimeoutKeyword_ClassifiesAsReadOnly_NotTimeout()
    {
        // Ensure a message like "read-only transaction timeout_user" doesn't get
        // misclassified as a timeout just because the read-only check comes first.
        var raw = new SqliteMessageDbException("Cannot execute in read-only transaction for session_timeout_user");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Insert);

        Assert.IsType<ReadOnlyViolationException>(result);
        Assert.IsNotType<CommandTimeoutException>(result);
    }

    // ── Write-write conflict detection (MVCC optimistic concurrency) ──────────
    //
    // DuckDB has no locking/deadlock concept (unlike Postgres/MySQL/SQL Server/Oracle, which
    // all map an analogous condition to DeadlockException/SerializationConflictException here).
    // Instead, two connections racing to modify the same row under DuckDB's MVCC model both
    // proceed until commit, and the loser's ExecuteNonQuery/Commit throws a DuckDBException whose
    // message starts with "TransactionContext Error: Conflict on ..." (confirmed empirically
    // against DuckDB.NET.Data.Full 1.4.1: "Conflict on update!" for two concurrent UPDATEs of the
    // same row, "Conflict on tuple deletion!" for two concurrent DELETEs). This is the real
    // failure mode a caller hits when e.g. a Hangfire job and a web request write to the same row
    // in a shared DuckDB file concurrently -- without this classification it fell through to
    // CreateFallback (IsTransient = null), giving the caller no signal that retrying is
    // appropriate, unlike every other multi-writer-capable dialect in this codebase.

    [Theory]
    [InlineData("TransactionContext Error: Conflict on update!")]
    [InlineData("TransactionContext Error: Conflict on tuple deletion!")]
    [InlineData("TransactionContext Error: Conflict on insert!")]
    public void DuckDb_WriteWriteConflictMessage_MapsTo_SerializationConflictException(string message)
    {
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
        Assert.Equal(true, result.IsTransient);
    }

    [Fact]
    public void DuckDb_WriteWriteConflict_WithTimeoutKeywordInMessage_ClassifiesAsConflict_NotTimeout()
    {
        // Same precedence guard as the other DuckDB categories: a conflict message must not be
        // misclassified as a timeout just because "timeout" appears in accompanying context.
        var raw = new SqliteMessageDbException("TransactionContext Error: Conflict on update! (session_timeout_job)");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
        Assert.IsNotType<CommandTimeoutException>(result);
    }

    // ── Cross-process file-lock detection (embedded, OS-level file lock) ──────

    [Fact]
    public void DuckDb_FileLockMessage_MapsTo_FileLockContentionException_AndIsNotTransient()
    {
        // Exact message confirmed empirically against DuckDB.NET.Data.Full 1.4.1: a second
        // process opening the same DuckDB file for read-write while a first still holds it open
        // fails at connection-open time with this message.
        //
        // This is deliberately NOT the same as the write-write MVCC conflict above. A conflict
        // clears on its own -- the loser can always retry and eventually win. A file lock only
        // clears if the other process closes its connection; if that "other process" is a second
        // long-running writer that was never supposed to exist against this file (the actual
        // single-node invariant DuckDB's embedded model assumes), retrying never succeeds. A
        // blanket IsTransient = true here would let a generic retry loop quietly spin on a
        // topology violation instead of surfacing it. So: its own exception type (still a
        // ConnectionException, since it does fail at connection-open time and existing
        // `catch (ConnectionException)` callers should still see it), non-transient by default --
        // a caller who has verified their specific deployment shape really is transient
        // contention (e.g. a Hangfire job briefly holding the file) can catch this type
        // specifically and choose to retry; nothing does so blindly.
        var raw = new SqliteMessageDbException(
            "IO Error: Could not set lock on file \"test.db\": Conflicting lock is held in other_process (PID 12345). " +
            "See also https://duckdb.org/docs/stable/connect/concurrency");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Query);

        Assert.IsType<FileLockContentionException>(result);
        Assert.IsAssignableFrom<ConnectionException>(result); // still catchable as a generic connection failure
        Assert.Equal(false, result.IsTransient);
    }
}
