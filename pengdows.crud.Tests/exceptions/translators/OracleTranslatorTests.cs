using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class OracleTranslatorTests
{
    private readonly OracleExceptionTranslator _translator = new();

    // ── Unique constraint ─────────────────────────────────────────────────────

    [Fact]
    public void UniqueViolation_ORA00001_Maps_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(1, "ORA-00001: unique constraint (SYSTEM.SYS_C008590) violated");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Not-null constraint ───────────────────────────────────────────────────

    [Fact]
    public void NotNull_ORA01400_Maps_NotNullViolationException()
    {
        var raw = new NumberedDbException(1400, "ORA-01400: cannot insert NULL into (\"SYSTEM\".\"TEST_TABLE\".\"NAME\")");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Foreign key constraint ────────────────────────────────────────────────

    [Fact]
    public void ForeignKey_ORA02291_Maps_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(2291, "ORA-02291: integrity constraint violated - parent key not found");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ForeignKey_ORA02292_Maps_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(2292, "ORA-02292: integrity constraint violated - child record found");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    // ── Check constraint ──────────────────────────────────────────────────────

    [Fact]
    public void Check_ORA02290_Maps_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(2290, "ORA-02290: check constraint (SYSTEM.CHK_VALUE_POSITIVE) violated");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Oracle, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    // ── Deadlock ──────────────────────────────────────────────────────────────

    [Fact]
    public void Deadlock_ORA00060_Maps_DeadlockException()
    {
        var raw = new NumberedDbException(60, "ORA-00060: deadlock detected while waiting for resource");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Serialization conflict ────────────────────────────────────────────────

    [Fact]
    public void SerializationConflict_ORA08177_Maps_SerializationConflictException()
    {
        var raw = new NumberedDbException(8177, "ORA-08177: can't serialize access for this transaction");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public void Timeout_TimeoutException_Maps_CommandTimeoutException()
    {
        var raw = new TimeoutException("query timed out");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    // ── Unknown ───────────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_Maps_DatabaseOperationException()
    {
        var raw = new NumberedDbException(4031, "ORA-04031: unable to allocate shared memory");

        var result = _translator.Translate(SupportedDatabase.Oracle, raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }
}
