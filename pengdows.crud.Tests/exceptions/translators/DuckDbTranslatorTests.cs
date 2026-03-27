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
}
