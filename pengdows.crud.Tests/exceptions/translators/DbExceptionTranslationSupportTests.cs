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
}
