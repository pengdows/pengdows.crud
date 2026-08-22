using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// Tests for internal DbExceptionTranslationSupport methods — accessed via InternalsVisibleTo.
/// </summary>
public class DbExceptionTranslationSupportTests
{
    // -------------------------------------------------------------------------
    // Custom exception types for reflection-based property tests
    // -------------------------------------------------------------------------

    private sealed class ShortNumberException : Exception
    {
        public short Number { get; } = 1234;
        public ShortNumberException(string msg) : base(msg) { }
    }

    private sealed class LongNumberException : Exception
    {
        public long Number { get; } = 50000L;
        public LongNumberException(string msg) : base(msg) { }
    }

    private sealed class LongNumberTooLargeException : Exception
    {
        // Exceeds int range — should map to null
        public long Number { get; } = (long)int.MaxValue + 1;
        public LongNumberTooLargeException(string msg) : base(msg) { }
    }

    private sealed class ConstraintPropertyException : Exception
    {
        public string ConstraintName { get; } = "uq_my_table_col";
        public ConstraintPropertyException(string msg) : base(msg) { }
    }

    private sealed class WrappedDbException : System.Data.Common.DbException
    {
        public WrappedDbException(string message, Exception inner) : base(message, inner) { }
    }

    // -------------------------------------------------------------------------
    // LooksLikeTimeout — must walk the InnerException chain
    // -------------------------------------------------------------------------

    [Fact]
    public void LooksLikeTimeout_InnerExceptionIsTimeoutException_ReturnsTrue()
    {
        // Regression: confirmed against a live Postgres container with CommandTimeout=1 —
        // Npgsql wraps a real client-side read timeout as
        // NpgsqlException("Exception while reading from stream") with InnerException
        // TimeoutException("Timeout during reading attempt"). The outer message contains no
        // "timeout" wording at all and the outer type name doesn't contain "Timeout" either, so
        // none of LooksLikeTimeout's three existing checks fire on the outer exception alone —
        // it must walk the InnerException chain.
        var inner = new TimeoutException("Timeout during reading attempt");
        var outer = new WrappedDbException("Exception while reading from stream", inner);

        var result = DbExceptionTranslationSupport.LooksLikeTimeout(outer);

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeTimeout_NoTimeoutAnywhereInChain_ReturnsFalse()
    {
        var inner = new InvalidOperationException("some unrelated failure");
        var outer = new WrappedDbException("wrapping failure", inner);

        var result = DbExceptionTranslationSupport.LooksLikeTimeout(outer);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // TryGetErrorCode — short Number property (line 60)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetErrorCode_ShortNumberProperty_ReturnsAsInt()
    {
        var ex = new ShortNumberException("short number error");

        var result = DbExceptionTranslationSupport.TryGetErrorCode(ex);

        Assert.Equal(1234, result);
    }

    // -------------------------------------------------------------------------
    // TryGetErrorCode — long Number property within int range (lines 61-62)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetErrorCode_LongNumberPropertyWithinIntRange_ReturnsAsInt()
    {
        var ex = new LongNumberException("long number error");

        var result = DbExceptionTranslationSupport.TryGetErrorCode(ex);

        Assert.Equal(50000, result);
    }

    [Fact]
    public void TryGetErrorCode_LongNumberPropertyOutOfIntRange_ReturnsNull()
    {
        var ex = new LongNumberTooLargeException("overflow error");

        var result = DbExceptionTranslationSupport.TryGetErrorCode(ex);

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // TryGetConstraintName — ConstraintName property (line 90)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetConstraintName_ConstraintNameProperty_ReturnsPropertyValue()
    {
        var ex = new ConstraintPropertyException("constraint violation");

        var result = DbExceptionTranslationSupport.TryGetConstraintName(ex);

        Assert.Equal("uq_my_table_col", result);
    }

    // -------------------------------------------------------------------------
    // TryGetConstraintName — empty message guard (line 96)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetConstraintName_EmptyMessage_ReturnsNull()
    {
        var ex = new InvalidOperationException(string.Empty);

        var result = DbExceptionTranslationSupport.TryGetConstraintName(ex);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetConstraintName_WhitespaceMessage_ReturnsNull()
    {
        var ex = new InvalidOperationException("   ");

        var result = DbExceptionTranslationSupport.TryGetConstraintName(ex);

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // Round-trip through FallbackExceptionTranslator to exercise CreateFallback
    // -------------------------------------------------------------------------

    [Fact]
    public void FallbackTranslator_WithShortNumberException_PreservesErrorCode()
    {
        var translator = new FallbackExceptionTranslator();
        var inner = new ShortNumberException("db error with short code");

        var result = translator.Translate(SupportedDatabase.Firebird, inner, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.Equal(1234, result.ErrorCode);
    }

    [Fact]
    public void FallbackTranslator_WithConstraintNameException_PreservesConstraintName()
    {
        var translator = new FallbackExceptionTranslator();
        var inner = new ConstraintPropertyException("constraint error");

        var result = translator.Translate(SupportedDatabase.DuckDB, inner, DbOperationKind.Insert);

        Assert.Equal("uq_my_table_col", result.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // TryGetSqlState — message fallback regex must accept alphanumeric SQLSTATEs
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetSqlState_MessageFallback_AllDigitState_TrailingSqlStateFormat_ReturnsState()
    {
        var ex = new InvalidOperationException("CLI0114E Datetime field overflow. SQLSTATE=22008");

        var result = DbExceptionTranslationSupport.TryGetSqlState(ex);

        Assert.Equal("22008", result);
    }

    [Fact]
    public void TryGetSqlState_MessageFallback_AlphanumericState_TrailingSqlStateFormat_ReturnsState()
    {
        // Regression: the regex previously required all-digit \d{5}, but real SQLSTATEs are
        // alphanumeric (e.g. "42S02" - table not found, "HY000" - general error).
        var ex = new InvalidOperationException("Some driver error. SQLSTATE=42S02");

        var result = DbExceptionTranslationSupport.TryGetSqlState(ex);

        Assert.Equal("42S02", result);
    }

    [Fact]
    public void TryGetSqlState_MessageFallback_AlphanumericState_LeadingErrorBracketFormat_ReturnsState()
    {
        var ex = new InvalidOperationException("ERROR [HY000] General driver error occurred");

        var result = DbExceptionTranslationSupport.TryGetSqlState(ex);

        Assert.Equal("HY000", result);
    }
}
