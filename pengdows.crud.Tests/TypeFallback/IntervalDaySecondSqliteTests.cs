using System;
using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// SQLite has no interval type. A day-time interval is bound as the ISO 8601 text the day-second converter reads back
/// exactly (days, hours, minutes, seconds and fraction), not as the value's display text, which the strict parser
/// refuses ("Not a valid day-to-second interval").
/// </summary>
public class IntervalDaySecondSqliteTests
{
    [Fact]
    public void OnSqliteTheIntervalIsBoundAsIsoTextThatReadsBackExactly()
    {
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var value = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));

        var parameter = dialect.CreateDbParameter("p", DbType.Object, value);

        Assert.Equal(DbType.String, parameter.DbType);
        var text = Assert.IsType<string>(parameter.Value);
        Assert.StartsWith("P", text, StringComparison.Ordinal);
        Assert.True(new IntervalDaySecondConverter().TryConvertFromProvider(text, SupportedDatabase.Sqlite, out var back));
        Assert.Equal(value, back);
    }
}
