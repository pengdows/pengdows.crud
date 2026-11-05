using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// IntervalDaySecond and IntervalYearMonth reach a PostgreSQL-family dialect as the value objects. Npgsql refuses an
/// unmapped value object, and refuses a string on an interval parameter (both confirmed live). PostgreSQL and
/// YugabyteDB therefore send the Npgsql interval the PostgreSqlInterval converter builds: months, days and microseconds.
/// CockroachDB takes ISO 8601 text. Npgsql isn't loaded in these unit tests, so the PostgreSQL-family value falls back to
/// ISO text here; the live fidelity matrix checks the driver's own value.
/// </summary>
public class PostgreSqlFamilyIntervalBindingTests
{
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void IntervalDaySecond_IsSentAsThePostgreSqlIntervalAtMicrosecondPrecision(SupportedDatabase product)
    {
        var dialect = Dialect(product);
        var value = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.Object, parameter.DbType);
        // PostgreSQL keeps microseconds: the 100-nanosecond digit is truncated, never rounded.
        var stored = IntervalDaySecond.Parse((string)parameter.Value!);
        Assert.Equal(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234560)), stored.TotalTime);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void IntervalYearMonth_IsSentAsThePostgreSqlIntervalWithTheSameMonths(SupportedDatabase product)
    {
        var dialect = Dialect(product);
        var value = new IntervalYearMonth(2, 3);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.Object, parameter.DbType);
        // The database normalises months to years and months; the total is what must match.
        Assert.Equal(value.TotalMonths, IntervalYearMonth.Parse((string)parameter.Value!).TotalMonths);
    }

    [Fact]
    public void IntervalDaySecond_OnCockroachDb_IsIsoTextThatParsesBack()
    {
        var dialect = Dialect(SupportedDatabase.CockroachDb);
        var value = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.IsType<string>(parameter.Value);
        Assert.Equal(value, IntervalDaySecond.Parse((string)parameter.Value!));
    }

    [Fact]
    public void IntervalYearMonth_OnCockroachDb_IsIsoTextThatParsesBack()
    {
        var dialect = Dialect(SupportedDatabase.CockroachDb);
        var value = new IntervalYearMonth(2, 3);

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.IsType<string>(parameter.Value);
        Assert.Equal(value, IntervalYearMonth.Parse((string)parameter.Value!));
    }

    private static SqlDialect Dialect(SupportedDatabase product) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(product, new fakeDbFactory(product), NullLogger.Instance);
}
