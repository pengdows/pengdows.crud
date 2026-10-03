using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// WRT-008, confirmed live (DuckDB.NET 1.5.6): a TimeSpan parameter whose type DuckDB can't infer
/// from a target column (a MERGE source's SELECT) is sent as TimeSpan.ToString() text
/// ("3.04:05:06.7890070"), which DuckDB can't cast to INTERVAL; a negative TimeSpan can't be bound
/// natively at all. DuckDB's own interval text works in INSERT, MERGE and WHERE alike.
/// </summary>
public sealed class DuckDbIntervalBindingTests
{
    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.DuckDB, new fakeDbFactory(SupportedDatabase.DuckDB),
            NullLogger.Instance);

    [Theory]
    [InlineData(3L * TimeSpan.TicksPerDay + 4 * TimeSpan.TicksPerHour + 5 * TimeSpan.TicksPerMinute + 6 * TimeSpan.TicksPerSecond + 7_890_070, "3 days 04:05:06.789007")]
    [InlineData(-(TimeSpan.TicksPerDay + TimeSpan.TicksPerHour + 30 * TimeSpan.TicksPerMinute), "-1 days -01:30:00.000000")]
    [InlineData(-30 * TimeSpan.TicksPerMinute, "0 days -00:30:00.000000")]
    [InlineData(0L, "0 days 00:00:00.000000")]
    public void IntervalTimeSpan_BindsAsDuckDbIntervalText(long ticks, string expected)
    {
        var dialect = Dialect();

        var p = dialect.CreateDbParameter("v", DbType.Object, TimeSpan.FromTicks(ticks));

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(expected, p.Value);
        Assert.Equal(expected, dialect.PrepareParameterValue(TimeSpan.FromTicks(ticks), DbType.Object));
    }

    [Fact]
    public void TimeColumn_TimeSpanStaysATime()
    {
        var p = Dialect().CreateDbParameter("v", DbType.Time, new TimeSpan(4, 5, 6));

        Assert.Equal(DbType.Time, p.DbType);
        Assert.IsType<TimeSpan>(p.Value);
    }
}
