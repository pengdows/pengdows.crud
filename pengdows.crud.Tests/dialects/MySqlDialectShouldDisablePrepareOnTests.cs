using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// MySqlDialect.ShouldDisablePrepareOn recognizes MySQL error 1295 (unsupported prepared
/// statement) and 1461 (max_prepared_stmt_count exceeded) two ways each: a message-text token
/// match, and a numeric error-code fallback (via the shared reflection-based
/// TryGetProviderErrorCode helper, checking a "Number" property) for drivers/messages that don't
/// carry the recognizable text. These tests exercise the numeric-code fallback path specifically —
/// the message-token path is exercised elsewhere via live-container-derived fixtures.
/// </summary>
public class MySqlDialectShouldDisablePrepareOnTests
{
    private static MySqlDialect Dialect() =>
        new(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

    private sealed class NumberedException : Exception
    {
        public int Number { get; }

        public NumberedException(int number, string message) : base(message)
        {
            Number = number;
        }
    }

    [Fact]
    public void ShouldDisablePrepareOn_ErrorCode1295_WithoutMessageToken_ReturnsTrue()
    {
        var ex = new NumberedException(1295, "some opaque driver-specific wording");

        Assert.True(Dialect().ShouldDisablePrepareOn(ex));
    }

    [Fact]
    public void ShouldDisablePrepareOn_ErrorCode1461_WithoutMessageToken_ReturnsTrue()
    {
        var ex = new NumberedException(1461, "some opaque driver-specific wording");

        Assert.True(Dialect().ShouldDisablePrepareOn(ex));
    }

    [Fact]
    public void ShouldDisablePrepareOn_UnrelatedErrorCode_ReturnsFalse()
    {
        var ex = new NumberedException(1046, "no database selected");

        Assert.False(Dialect().ShouldDisablePrepareOn(ex));
    }

    [Fact]
    public void ShouldDisablePrepareOn_WalksInnerExceptionChainForErrorCode()
    {
        // The outer exception is deliberately NOT NotSupportedException/InvalidOperationException
        // (SqlDialect.ShouldDisablePrepareOn's own base-class check would short-circuit true for
        // either of those before ever reaching MySqlDialect's error-code walk, which would make
        // this test pass without actually exercising the inner-exception-chain fallback it's
        // meant to cover).
        var inner = new NumberedException(1461, "opaque driver wording, no recognizable token");
        var outer = new Exception("outer wrapper with no Number property", inner);

        Assert.True(Dialect().ShouldDisablePrepareOn(outer));
    }
}
