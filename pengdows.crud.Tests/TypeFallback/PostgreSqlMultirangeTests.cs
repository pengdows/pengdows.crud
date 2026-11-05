using System;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// A multirange (an array of ranges) is sent as an array typed to the driver's range type (Npgsql's NpgsqlRange, so the
/// server sees an int4multirange), and read back from the driver's array of ranges, each converted to a Range.
/// </summary>
public class PostgreSqlMultirangeTests
{
    [Fact]
    public void TheRangesFormATypedArray()
    {
        var typed = PostgreSqlMultirange.TypedArray(new object[] { new Range<int>(1, 5, true, false), new Range<int>(7, 9, true, false) });

        Assert.IsType<Range<int>[]>(typed);
        Assert.Equal(2, ((Array)typed).Length);
    }

    [Fact]
    public void TheDriversArrayOfRangesReadsAsTheRanges()
    {
        var driverArray = new object[] { new Range<int>(1, 5, true, false), new Range<int>(7, 9, true, false) };

        Assert.True(new PostgreSqlMultirangeCoercion(SupportedDatabase.PostgreSql).TryRead(new DbValue(driverArray), out var ranges));

        Assert.Equal(new[] { new Range<int>(1, 5, true, false), new Range<int>(7, 9, true, false) }, ranges);
    }
}
