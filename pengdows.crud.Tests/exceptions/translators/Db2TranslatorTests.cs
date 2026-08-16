using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class Db2TranslatorTests
{
    private readonly Db2ExceptionTranslator _translator = new();

    // ── SQLSTATE-based classification (properly-cased SqlState property) ───────

    [Fact]
    public void SqlState23505_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new SqlStateDbException("23505", "duplicate key value violates unique constraint");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState23503_MapsTo_ForeignKeyViolationException()
    {
        var raw = new SqlStateDbException("23503", "insert or update violates foreign key constraint");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void SqlState23502_MapsTo_NotNullViolationException()
    {
        var raw = new SqlStateDbException("23502", "null value violates not-null constraint");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SqlState23513_MapsTo_CheckConstraintViolationException()
    {
        // Note: Db2 uses 23513 for check-constraint violations — NOT 23514 like Postgres/DuckDB.
        var raw = new SqlStateDbException("23513", "new row violates check constraint");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState40001_MapsTo_SerializationConflictException()
    {
        var raw = new SqlStateDbException("40001", "deadlock or timeout");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    [Fact]
    public void UnknownSqlState_MapsTo_DatabaseOperationException()
    {
        var raw = new SqlStateDbException("58004", "internal error");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void TimeoutMessage_MapsTo_CommandTimeoutException()
    {
        var raw = new TimeoutException("query timed out");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
    }

    // ── Regression: IBM.Data.Db2's real DB2Exception shape ─────────────────────
    // Confirmed against a live ibmcom/db2 container during Phase 2 testbed validation:
    // a real duplicate-key insert produced a generic DatabaseOperationException instead of
    // UniqueConstraintViolationException until DbExceptionTranslationSupport.TryGetSqlState
    // was fixed to (a) match "SQLState" case-insensitively without throwing
    // AmbiguousMatchException against the inherited DbException.SqlState, and (b) fall back
    // to parsing "SQLSTATE=23505" out of the message text.

    [Fact]
    public void AllCapsSQLStateProperty_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new AllCapsSqlStateDbException("23505", "IBM-style exception");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void SqlStateOnlyInMessageText_MapsTo_UniqueConstraintViolationException()
    {
        // No structured SqlState/SQLState property at all — exactly like the real
        // DB2Exception.Message format observed against the live container.
        var raw = new PlainMessageDbException(
            "ERROR [23505] [IBM][DB2/LINUXX8664] SQL0803N  duplicate key.  SQLSTATE=23505");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    private sealed class AllCapsSqlStateDbException : DbException
    {
        public string SQLState { get; }

        public AllCapsSqlStateDbException(string sqlState, string message) : base(message)
        {
            SQLState = sqlState;
        }
    }

    private sealed class PlainMessageDbException : DbException
    {
        public PlainMessageDbException(string message) : base(message)
        {
        }
    }
}
