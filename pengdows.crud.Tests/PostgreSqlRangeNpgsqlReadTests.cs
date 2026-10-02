using System;
using pengdows.crud.enums;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002: DataReaderMapper's coercer for an object-typed value goes through CoercionRegistry;
/// an NpgsqlRange (what Npgsql returns for a range column) must map to the matching Range, not
/// fall through to Range.Empty.
/// </summary>
public class PostgreSqlRangeNpgsqlReadTests
{
    [Fact]
    public void ObjectCoercer_NpgsqlRangeOfInt_MapsToRange()
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(object), typeof(Range<int>), EnumParseFailureMode.Throw);

        var result = coercer(new NpgsqlTypes.NpgsqlRange<int>
            { LowerBound = 1, UpperBound = 10, LowerBoundIsInclusive = true, UpperBoundIsInclusive = false });

        Assert.Equal(new Range<int>(1, 10, true, false), result);
    }

    [Fact]
    public void ObjectCoercer_NpgsqlRangeOfLong_MapsToRange()
    {
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(object), typeof(Range<long>), EnumParseFailureMode.Throw);

        var result = coercer(new NpgsqlTypes.NpgsqlRange<long>
            { LowerBound = -5, UpperBound = long.MaxValue, LowerBoundIsInclusive = true, UpperBoundIsInclusive = true });

        Assert.Equal(new Range<long>(-5, long.MaxValue, true, true), result);
    }

    [Fact]
    public void ObjectCoercer_NpgsqlRangeOfDateTime_MapsToRange()
    {
        var lower = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var upper = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var coercer = TypeCoercionHelper.ResolveCoercer(typeof(object), typeof(Range<DateTime>), EnumParseFailureMode.Throw);

        var result = coercer(new NpgsqlTypes.NpgsqlRange<DateTime>
            { LowerBound = lower, UpperBound = upper, LowerBoundIsInclusive = true, UpperBoundIsInclusive = false });

        Assert.Equal(new Range<DateTime>(lower, upper, true, false), result);
    }
}
