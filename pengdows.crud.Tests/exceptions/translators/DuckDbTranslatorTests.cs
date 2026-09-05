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

    // ── Connection failure (file-open failure — DuckDB is embedded, no TCP concept) ─────────

    [Fact]
    public void ConnectionFailure_CannotOpenFileMessage_Maps_ConnectionException()
    {
        // Regression: confirmed against a real DuckDBException opening a nonexistent database
        // path. DuckDBException.ErrorType reports the generic "Invalid" value here (NOT a more
        // specific "Io"/"Connection" enum member), so message text is the only reliable trigger.
        var raw = new SqliteMessageDbException(
            "DuckDBOpen failed: IO Error: Cannot open file \"/nonexistent_dir/db.duckdb\": No such file or directory");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    // ── Serialization conflict ────────────────────────────────────────────────

    [Fact]
    public void ConflictOnUpdateMessage_Maps_SerializationConflictException()
    {
        // Regression: confirmed against a real DuckDBException from two concurrent transactions
        // conflicting on the same row — ErrorType reports "Transaction" (not "Serialization",
        // despite that enum member's name), message is "TransactionContext Error: Conflict on
        // update!". Message text is the reliable trigger here, not ErrorType.
        var raw = new SqliteMessageDbException("TransactionContext Error: Conflict on update!");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    [Theory]
    [InlineData("TransactionContext Error: Conflict on tuple deletion!")]
    [InlineData("TransactionContext Error: Conflict on insert!")]
    public void ConflictOnOtherOperationMessage_Maps_SerializationConflictException(string message)
    {
        // The existing update-conflict check above only matches "Conflict on update" literally,
        // so it misses the same MVCC conflict on DELETE/INSERT. Confirmed empirically against
        // DuckDB.NET.Data.Full 1.4.1: two concurrent DELETEs of the same row throw
        // "TransactionContext Error: Conflict on tuple deletion!" -- same failure family, same
        // retry-appropriate semantics, but previously fell through to the non-transient fallback.
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Delete);

        Assert.IsType<SerializationConflictException>(result);
        Assert.Equal(true, result.IsTransient);
    }

    // ── Cross-process file-lock (distinct from the file-open failure above) ───

    [Fact]
    public void FileLockMessage_Maps_ConnectionException_AndIsTransient()
    {
        // Distinct from ConnectionFailure_CannotOpenFileMessage_Maps_ConnectionException above:
        // that one is a missing/inaccessible path ("Cannot open file"), non-retryable. This is a
        // second process holding the OS-level file lock on an existing, valid DuckDB file --
        // confirmed empirically against DuckDB.NET.Data.Full 1.4.1 by actually racing two
        // processes for the same file. Unlike the missing-path case, this legitimately succeeds
        // on retry once the lock holder closes its connection -- exactly the shape of a Hangfire
        // worker and a web request both touching the same DuckDB-backed tenant file.
        var raw = new SqliteMessageDbException(
            "DuckDBOpen failed: IO Error: Could not set lock on file \"test.db\": Conflicting lock is held in other_process (PID 12345). " +
            "See also https://duckdb.org/docs/stable/connect/concurrency");

        var result = _translator.Translate(SupportedDatabase.DuckDB, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(true, result.IsTransient);
    }
}
