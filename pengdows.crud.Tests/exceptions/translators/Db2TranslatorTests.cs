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
    public void SqlState23504_MapsTo_ForeignKeyViolationException()
    {
        // Regression: confirmed against a live ibmcom/db2 container — deleting a parent row
        // blocked by a RESTRICT foreign key reports SQLSTATE 23504, NOT 23503 (which is only
        // used for insert/update FK violations). Both are still SQLCODE -532.
        var raw = new SqlStateDbException("23504",
            "a parent row cannot be deleted because the relationship restricts the deletion");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Delete);

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
    public void SqlState08001_MapsTo_ConnectionException()
    {
        // Regression: confirmed against a live IBM.Data.Db2 connect attempt to a closed TCP
        // port — message "ERROR [08001] [IBM] SQL30081N ... SQLSTATE=08001" (ANSI
        // connection-exception class 08).
        var raw = new SqlStateDbException("08001", "A communication error has been detected.");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void SqlStateOnlyInMessageText_08001_MapsTo_ConnectionException()
    {
        var raw = new PlainMessageDbException(
            "ERROR [08001] [IBM] SQL30081N  A communication error has been detected. " +
            "Communication protocol being used: \"TCP/IP\".  SQLSTATE=08001");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
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

    [Fact]
    public void SqlStateOnlyInMessageText_LeadingBracketFormOnly_MapsTo_NotNullViolationException()
    {
        // Server-side errors (constraint violations, etc.) use ONLY the leading "ERROR [nnnnn]"
        // form — there is no trailing "SQLSTATE=nnnnn" fragment, unlike client-side CLI driver
        // errors (e.g. CLI0114E). Confirmed against a live ibmcom/db2 container: a real NOT NULL
        // violation's message is exactly this shape, with nothing after "is not allowed."
        var raw = new PlainMessageDbException(
            "ERROR [23502] [IBM][DB2/LINUXX8664] SQL0407N  Assignment of a NULL value to a " +
            "NOT NULL column \"TBSPACEID=2, TABLEID=4, COLNO=1\" is not allowed.");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SqlStateOnlyInMessageText_TrailingEqualsFormOnly_MapsTo_ForeignKeyViolationException()
    {
        // Client-side CLI driver errors (e.g. CLI0114E) use ONLY the trailing "SQLSTATE=nnnnn"
        // form — no leading "ERROR [nnnnn]" bracket. This message deliberately has NO bracket
        // form at all, so a correct classification here proves the trailing-form regex branch
        // works standalone, not just when both forms happen to co-occur in the same message.
        var raw = new PlainMessageDbException(
            "CLI0125E  Some wrapping driver message with no bracket form. SQLSTATE=23503");

        var result = _translator.Translate(SupportedDatabase.Db2, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
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
