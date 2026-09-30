using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-006 (maintainer decision 2026-09-30): Spanner has no time-of-day column type (no TIME;
/// INTERVAL is query-only), so a TimeOnly (or a TimeSpan declared DbType.Time) is stored as
/// fixed-width text HH:mm:ss.fffffff in a STRING/VARCHAR column. Fixed width keeps text order equal
/// to time order, so ORDER BY and range predicates stay correct; reads parse it back exactly.
/// </summary>
public sealed class SpannerTimeOnlyTextTests
{
    private static SqlDialect Spanner() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.Spanner,
            new fakeDbFactory(SupportedDatabase.Spanner), NullLogger<SqlDialect>.Instance);

    [Theory]
    [InlineData(13, 45, 30, 1_234_567, "13:45:30.1234567")]
    [InlineData(0, 0, 0, 0, "00:00:00.0000000")]
    [InlineData(23, 59, 59, 9_999_999, "23:59:59.9999999")]
    [InlineData(6, 0, 1, 0, "06:00:01.0000000")]
    public void TimeOnly_BindsAsFixedWidthText(int h, int m, int s, int ticks, string expected)
    {
        var value = new TimeOnly(h, m, s).Add(TimeSpan.FromTicks(ticks));

        var parameter = Spanner().CreateDbParameter("t", DbType.Time, value);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(expected, parameter.Value);
    }

    [Fact]
    public void TimeSpanDeclaredAsTime_BindsAsTheSameText()
    {
        var parameter = Spanner().CreateDbParameter("t", DbType.Time, new TimeSpan(0, 13, 45, 30));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("13:45:30.0000000", parameter.Value);
    }

    [Fact]
    public void TimeSpanOutsideADay_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Spanner().CreateDbParameter("t", DbType.Time, TimeSpan.FromHours(25)));
    }

    [Fact]
    public void TheStoredText_ReadsBackExactly()
    {
        var value = new TimeOnly(13, 45, 30).Add(TimeSpan.FromTicks(1_234_567));

        var read = TypeCoercionHelper.Coerce("13:45:30.1234567", typeof(string), typeof(TimeOnly));

        Assert.Equal(value, read);
    }

    [Fact]
    public void OtherPostgreSqlFamilyDialects_AreUnchanged()
    {
        var postgres = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.PostgreSql,
            new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<SqlDialect>.Instance);

        Assert.NotEqual(DbType.String, postgres.CreateDbParameter("t", DbType.Time, new TimeOnly(1, 2, 3)).DbType);
    }
}
