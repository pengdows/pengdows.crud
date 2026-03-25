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
}
