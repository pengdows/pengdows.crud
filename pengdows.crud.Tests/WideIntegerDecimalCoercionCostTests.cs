using System;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Threading;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DuckDB HUGEINT, Firebird INT128 and an overflowed Snowflake NUMBER arrive as BigInteger/Int128/
/// UInt128 (TYPE-005). Mapped to a decimal property, the registered decimal coercion called
/// Convert.ToDecimal, which throws for a type that isn't IConvertible, before a fallback converted it:
/// one exception per value, measured at about 10 µs per row against 74 ns for a plain long.
/// </summary>
public sealed class WideIntegerDecimalCoercionCostTests
{
    public static TheoryData<object, decimal> WideValues() => new()
    {
        { new BigInteger(1_000_000_000_000L), 1_000_000_000_000m },
        { (Int128)(-123_456_789_012_345L), -123_456_789_012_345m },
        { (UInt128)ulong.MaxValue, 18446744073709551615m },
    };

    [Theory]
    [MemberData(nameof(WideValues))]
    public void MapperCoercer_WideIntegerToDecimal_ConvertsWithoutThrowing(object value, decimal expected)
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(value.GetType(), typeof(decimal), EnumParseFailureMode.Throw, null);
        var thread = Environment.CurrentManagedThreadId;
        var thrown = 0;
        void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                Interlocked.Increment(ref thrown);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        object? result;
        try
        {
            result = coercer(value);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        }

        Assert.Equal(expected, result);
        Assert.Equal(0, thrown);
    }

    [Fact]
    public void MapperCoercer_WideIntegerBeyondDecimal_StillFails()
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(BigInteger), typeof(decimal), EnumParseFailureMode.Throw, null);

        Assert.ThrowsAny<Exception>(() => coercer(BigInteger.Pow(10, 40)));
    }
}
